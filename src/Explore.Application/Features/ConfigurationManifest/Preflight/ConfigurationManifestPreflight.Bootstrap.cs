using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Domain.Services.Registration;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Preflight;

/// <summary>Values projected from the original native bootstrap observation, not a write grant.</summary>
public sealed record ConfigurationManifestBootstrapSnapshot(
    ImmutableArray<Tenant> Tenants, ImmutableArray<SystemSetting> Systems,
    ImmutableArray<TenantSetting> Overrides, ImmutableArray<PaidEventPolicyVersion> Policies,
    ConfigurationManifestBootstrapState? BootstrapState);

public sealed partial class ConfigurationManifestPreflight
{
    public static ConfigurationManifestPreflightResult EvaluateOriginal(
        ConfigurationManifestApplyPlan original, ConfigurationManifestBootstrapSnapshot source)
    {
        var lifecycle = ResolveBootstrapLifecycle(original with { BootstrapState = source.BootstrapState });
        var plan = lifecycle.BoundPlan;
        if (lifecycle.Error is { } error) return new(plan, [], [error]);
        var errors = new List<ConfigurationManifestPreflightError>();
        var bySlug = source.Tenants.ToDictionary(tenant => tenant.Slug, StringComparer.Ordinal);
        var tenants = plan.Tenants.Select(tenant => bySlug.TryGetValue(tenant.Slug, out var existing)
            ? new ConfigurationManifestPreflightTenant(tenant, ConfigurationManifestTenantDisposition.SkippedExisting, existing.Id)
            : new ConfigurationManifestPreflightTenant(tenant, ConfigurationManifestTenantDisposition.Create, tenant.PlannedTenantId))
            .ToImmutableArray();
        var systems = source.Systems.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var create = tenants.Where(tenant => tenant.Disposition == ConfigurationManifestTenantDisposition.Create).ToArray();
        var currentPolicy = source.Policies.SingleOrDefault(policy => policy.TenantId is null);
        PaidEventPolicyVersion? ceiling = currentPolicy;
        if (plan.Instance.PaidEventPolicy is { } paid)
        {
            if (currentPolicy is null)
                errors.Add(new(-1, ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                    ConfigurationManifestApplicationFailureCodes.PaidPolicyUnavailable, "An active instance paid policy is required."));
            else
            {
                plan = plan with
                {
                    Instance = plan.Instance with
                    { PaidEventPolicy = paid with { ExpectedActivePolicyVersion = currentPolicy.VersionNumber } }
                };
                if (paid.ProposedRevision is not null)
                {
                    try
                    {
                        ceiling = ConfigurationManifestPaidEventPolicyMapper.CreateInstanceCandidate(paid.ProposedRevision);
                        foreach (var existing in source.Policies.Where(policy => policy.TenantId.HasValue))
                            PaidEventPolicyRules.ValidateTenantPolicy(ceiling, existing);
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                    {
                        errors.Add(new(-1, ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                            ConfigurationManifestApplicationFailureCodes.PaidPolicyBroadening, "The proposed instance policy excludes an existing tenant."));
                    }
                }
            }
        }
        var publicationSystems = source.Systems.Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey))
            .Select(row => new PublicationPolicySystemValueSnapshot(row.SettingKey, row.Value, row.IsLocked)).ToImmutableArray();
        var publicationTenants = source.Overrides.Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey))
            .Select(row => new PublicationPolicyTenantValueSnapshot(row.TenantId, row.SettingKey, row.Value)).ToImmutableArray();
        var instancePublication = plan.Instance.GuardedSettings
            .Where(setting => PublicationPolicySettingKeys.All.Contains(setting.Key)).ToImmutableArray();
        if (!instancePublication.IsEmpty)
        {
            var compiled = PublicationPolicyProposedStateCompiler.CompileInstance(
                new(publicationSystems, publicationTenants, ToInstancePublicationMutations(instancePublication)));
            if (!compiled.Success || compiled.BaseTenantState is not { } state)
                AddInvalidPublicationPolicyError(-1, instancePublication[0].Key, errors);
            else
            {
                var checks = compiled.TenantStates.Select(tenant => ReportingIntakePolicyEvaluator.Evaluate(tenant.State))
                    .Prepend(ReportingIntakePolicyEvaluator.Evaluate(state));
                foreach (var rejected in checks.Where(check => !check.Allowed))
                    errors.Add(new(-1, instancePublication[0].Key, rejected.ReasonCode, rejected.Message));
            }
        }
        _ = TryApplyInstanceSettings(publicationSystems, instancePublication, out var proposedSystems);
        var resourceValues = EventResourceSettingMutationGuard.Keys.ToDictionary(key => key,
            key => systems.GetValueOrDefault(key)?.Value ?? SettingRegistry.Get(key)!.DefaultValue, StringComparer.Ordinal);
        foreach (var setting in plan.Instance.GuardedSettings.Where(setting => EventResourceSettingMutationGuard.Handles(setting.Key)))
            resourceValues[setting.Key] = setting.JsonValue;
        EventResourceGovernancePolicy? resourceCeiling = null;
        bool hasResources = plan.Instance.GuardedSettings.Any(setting => EventResourceSettingMutationGuard.Handles(setting.Key))
            || create.Any(tenant => tenant.Plan.GuardedSettings.Any(setting => EventResourceSettingMutationGuard.Handles(setting.Key)));
        try { if (hasResources) resourceCeiling = EventResourceGovernancePolicyValues.Parse(resourceValues, long.MaxValue); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            errors.Add(new(-1, "instance.settings", "event_resource_policy_invalid", "The proposed resource policy is invalid."));
        }
        foreach (var candidate in create)
        {
            var tenant = candidate.Plan;
            var locks = tenant.GuardedSettings.Concat(tenant.UnguardedSettings).ToDictionary(setting => setting.Key,
                setting => systems.GetValueOrDefault(setting.Key)?.IsLocked == true, StringComparer.Ordinal);
            AddLockedSettingErrors(tenant, locks, errors);
            AddPaidEventPolicyErrors(tenant, ceiling, errors);
            var publication = tenant.GuardedSettings.Where(setting => PublicationPolicySettingKeys.All.Contains(setting.Key))
                .Select(setting => new PublicationPolicySettingMutation(setting.Key, PublicationPolicyMutationKind.Set,
                    setting.JsonValue, tenant.PlannedTenantId, null)).ToImmutableArray();
            if (!publication.IsEmpty)
            {
                var compiled = PublicationPolicyProposedStateCompiler.CompileTenant(
                    new(tenant.PlannedTenantId, proposedSystems, [], publication));
                if (!compiled.Success)
                    AddInvalidPublicationPolicyError(tenant.ManifestIndex, publication[0].Key, errors);
                else
                {
                    var evaluation = ReportingIntakePolicyEvaluator.Evaluate(compiled.TenantStates.Single().State);
                    if (!evaluation.Allowed)
                        errors.Add(new(tenant.ManifestIndex, publication[0].Key, evaluation.ReasonCode, evaluation.Message));
                }
            }
            foreach (var setting in tenant.GuardedSettings.Where(setting => EventResourceSettingMutationGuard.Handles(setting.Key)))
            {
                try
                {
                    var proposed = new Dictionary<string, string>(resourceValues, StringComparer.Ordinal) { [setting.Key] = setting.JsonValue };
                    if (resourceCeiling is not null
                        && !resourceCeiling.IsNonWidening(EventResourceGovernancePolicyValues.Parse(proposed, long.MaxValue)))
                        errors.Add(new(tenant.ManifestIndex, setting.Key, "event_resource_policy_widening",
                            "The tenant resource policy exceeds the proposed instance ceiling."));
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    errors.Add(new(tenant.ManifestIndex, setting.Key, "event_resource_policy_invalid", "The tenant resource policy is invalid."));
                }
            }
            if (JsonSerializer.Deserialize<string>(systems.GetValueOrDefault(GovernanceSettingKeys.Deployment.Mode)?.Value
                    ?? "\"SingleTenant\"")?.Equals("MultiTenant", StringComparison.OrdinalIgnoreCase) == true)
            {
                bool whiteLabel = JsonSerializer.Deserialize<bool>(
                    systems.GetValueOrDefault(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled)?.Value ?? "false");
                var requested = JsonSerializer.Deserialize<BrandingSettings>(tenant.BrandingDocument.PayloadJson, SerializerOptions)!;
                if (Blocked(GovernanceSettingKeys.Branding.DisplayName, tenant.DisplayName, requested.DisplayName, true)
                    || Blocked(GovernanceSettingKeys.Branding.LogoUrl, null, requested.LogoUrl, whiteLabel)
                    || Blocked(GovernanceSettingKeys.Branding.FaviconUrl, null, requested.FaviconUrl, whiteLabel)
                    || Blocked(GovernanceSettingKeys.Branding.CustomCssUrl, null, requested.CustomCssUrl, whiteLabel))
                    errors.Add(new(tenant.ManifestIndex, SettingsDocumentKeys.Tenant.Branding,
                        ConfigurationManifestApplicationFailureCodes.DocumentLocked, "Instance governance locks this branding change."));
            }
        }
        return new(plan, tenants, errors.OrderBy(error => error.ManifestIndex).ThenBy(error => error.Key, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal).ToImmutableArray());

        bool Blocked(string key, string? before, string? after, bool enabled) =>
            (!enabled || systems.GetValueOrDefault(key)?.IsLocked == true)
            && (string.IsNullOrWhiteSpace(before) ? null : before.Trim()) != (string.IsNullOrWhiteSpace(after) ? null : after.Trim());
    }
}
