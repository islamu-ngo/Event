namespace Explore.Application.Features.ConfigurationManifest.Preflight;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

public sealed class ConfigurationManifestPreflight(
    ITenantRepository tenantRepository,
    ISystemSettingRepository systemSettingRepository,
    ITenantBrandingSettingsDocumentLockService brandingLockService,
    ICoordinatedSettingMutationStore policyStore,
    IPaidEventPolicyRepository paidEventPolicies)
    : IConfigurationManifestPreflight
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ConfigurationManifestPreflightResult> EvaluateAsync(
        ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        BootstrapLifecycleResolution lifecycle =
            ResolveBootstrapLifecycle(plan);
        plan = lifecycle.BoundPlan;
        if (lifecycle.Error is not null)
        {
            return new ConfigurationManifestPreflightResult(
                plan,
                [],
                [lifecycle.Error]);
        }

        string[] slugs = plan.Tenants.Select(tenant => tenant.Slug).ToArray();
        IReadOnlyList<Tenant> existing = await tenantRepository.GetBySlugsAsNoTrackingAsync(
            slugs,
            cancellationToken);
        Dictionary<string, Tenant> existingBySlug = existing.ToDictionary(
            tenant => tenant.Slug,
            StringComparer.Ordinal);
        ImmutableArray<ConfigurationManifestPreflightTenant> tenants = plan.Tenants
            .Select(tenant => existingBySlug.TryGetValue(tenant.Slug, out Tenant? found)
                ? new ConfigurationManifestPreflightTenant(
                    tenant,
                    ConfigurationManifestTenantDisposition.SkippedExisting,
                    found.Id)
                : new ConfigurationManifestPreflightTenant(
                    tenant,
                    ConfigurationManifestTenantDisposition.Create,
                    tenant.PlannedTenantId))
            .ToImmutableArray();
        ConfigurationManifestPreflightTenant[] createCandidates = tenants
            .Where(tenant => tenant.Disposition == ConfigurationManifestTenantDisposition.Create)
            .ToArray();

        var errors = new List<ConfigurationManifestPreflightError>();
        PaidEventPolicyAuthorityResolution paidEventPolicyAuthority =
            await ResolvePaidEventPolicyAuthorityAsync(
                plan,
                createCandidates,
                errors,
                cancellationToken);
        plan = paidEventPolicyAuthority.BoundPlan;
        await AddInstancePublicationPolicyErrorsAsync(
            plan.Instance,
            errors,
            cancellationToken);
        if (createCandidates.Length == 0)
        {
            return new ConfigurationManifestPreflightResult(
                plan,
                tenants,
                [.. errors]);
        }

        Dictionary<string, bool> locks = await ReadSettingLocksAsync(
            createCandidates,
            cancellationToken);
        TenantBrandingSettingsDocumentLockState brandingLock =
            await brandingLockService.GetLockStateAsync(cancellationToken);
        foreach (ConfigurationManifestPreflightTenant candidate in createCandidates)
        {
            AddLockedSettingErrors(candidate.Plan, locks, errors);
            AddBrandingErrors(candidate.Plan, brandingLock, errors);
            AddPaidEventPolicyErrors(
                candidate.Plan,
                paidEventPolicyAuthority.EffectiveInstancePolicy,
                errors);
            await AddPublicationPolicyErrorsAsync(
                plan.Instance,
                candidate.Plan,
                errors,
                cancellationToken);
        }

        return new ConfigurationManifestPreflightResult(
            plan,
            tenants,
            errors
                .OrderBy(error => error.ManifestIndex)
                .ThenBy(error => error.Key, StringComparer.Ordinal)
                .ThenBy(error => error.Code, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static BootstrapLifecycleResolution ResolveBootstrapLifecycle(
        ConfigurationManifestApplyPlan plan)
    {
        ConfigurationManifestBootstrapState? state = plan.BootstrapState;
        if (state is null)
        {
            return new BootstrapLifecycleResolution(plan, Error: null);
        }

        if (state.Generation <= 0
            || state.InstanceSectionDigest.Length
                != ConfigurationManifestOperation.DigestLength
            || state.InstanceSectionDigest.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            return new BootstrapLifecycleResolution(
                plan,
                new ConfigurationManifestPreflightError(
                    ManifestIndex: -1,
                    "spec.instance",
                    ConfigurationManifestApplicationFailureCodes
                        .BootstrapStateInvalid,
                    "The persisted configuration-manifest bootstrap state is invalid."));
        }

        if (!string.Equals(
                state.InstanceSectionDigest,
                plan.InstanceSectionDigest,
                StringComparison.Ordinal))
        {
            return new BootstrapLifecycleResolution(
                plan,
                new ConfigurationManifestPreflightError(
                    ManifestIndex: -1,
                    "spec.instance",
                    ConfigurationManifestApplicationFailureCodes
                        .InstanceAlreadyBootstrapped,
                    "The instance section was already bootstrapped; use Day 2 administration for instance changes."));
        }

        ConfigurationManifestInstancePaidEventPolicyPlan? paidPolicy =
            plan.Instance.PaidEventPolicy is null
                ? null
                : plan.Instance.PaidEventPolicy with
                {
                    ProposedRevision = null,
                    ExpectedActivePolicyVersion = null
                };
        ConfigurationManifestApplyPlan bound = plan with
        {
            Instance = plan.Instance with
            {
                GuardedSettings = [],
                UnguardedSettings = [],
                PaidEventPolicy = paidPolicy,
                ChangedSettingKeyNames = [],
                ChangedDocumentKeyNames = []
            }
        };
        return new BootstrapLifecycleResolution(bound, Error: null);
    }

    private sealed record BootstrapLifecycleResolution(
        ConfigurationManifestApplyPlan BoundPlan,
        ConfigurationManifestPreflightError? Error);

    private async Task<PaidEventPolicyAuthorityResolution>
        ResolvePaidEventPolicyAuthorityAsync(
        ConfigurationManifestApplyPlan plan,
        IReadOnlyCollection<ConfigurationManifestPreflightTenant> createCandidates,
        ICollection<ConfigurationManifestPreflightError> errors,
        CancellationToken cancellationToken)
    {
        bool isRequired =
            plan.Instance.PaidEventPolicy?.ProposedRevision is not null
            || createCandidates.Any(candidate =>
                candidate.Plan.PaidEventPolicy is not null);
        if (!isRequired)
        {
            return new PaidEventPolicyAuthorityResolution(
                plan,
                EffectiveInstancePolicy: null);
        }

        PaidEventPolicyVersion? current =
            await paidEventPolicies.GetActiveInstanceAsync(cancellationToken);
        if (current is null || !current.IsActive || current.TenantId is not null)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                ManifestIndex: -1,
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                ConfigurationManifestApplicationFailureCodes.PaidPolicyUnavailable,
                "An active instance paid-event policy is required."));
            return new PaidEventPolicyAuthorityResolution(
                plan,
                EffectiveInstancePolicy: null);
        }

        ConfigurationManifestInstancePaidEventPolicyPlan authority =
            plan.Instance.PaidEventPolicy
            ?? throw new InvalidOperationException(
                "Paid-event policy documents require compiled instance authority.");
        if (authority.ExpectedActivePolicyVersion is { } expectedCurrentVersion
            && current.VersionNumber != expectedCurrentVersion)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                ManifestIndex: -1,
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                ConfigurationManifestApplicationFailureCodes.PaidPolicyStale,
                "The paid-event policy authority changed after manifest compilation."));
            return new PaidEventPolicyAuthorityResolution(
                plan,
                EffectiveInstancePolicy: null);
        }

        if (!authority.ExpectedActivePolicyVersion.HasValue)
        {
            authority = authority with
            {
                ExpectedActivePolicyVersion = current.VersionNumber
            };
            plan = plan with
            {
                Instance = plan.Instance with
                {
                    PaidEventPolicy = authority
                }
            };
        }

        if (authority.ProposedRevision is null)
        {
            return new PaidEventPolicyAuthorityResolution(plan, current);
        }

        try
        {
            PaidEventPolicyVersion proposed =
                ConfigurationManifestPaidEventPolicyMapper
                    .CreateInstanceCandidate(authority.ProposedRevision);
            return new PaidEventPolicyAuthorityResolution(plan, proposed);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                ManifestIndex: -1,
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                ConfigurationManifestApplicationFailureCodes.PaidPolicyBroadening,
                "The instance paid-event policy is invalid."));
            return new PaidEventPolicyAuthorityResolution(
                plan,
                EffectiveInstancePolicy: null);
        }
    }

    private static void AddPaidEventPolicyErrors(
        ConfigurationManifestTenantPlan tenant,
        PaidEventPolicyVersion? effectiveInstancePolicy,
        ICollection<ConfigurationManifestPreflightError> errors)
    {
        if (tenant.PaidEventPolicy is null)
        {
            return;
        }

        if (effectiveInstancePolicy is null)
        {
            return;
        }

        try
        {
            PaidEventPolicyVersion candidate =
                ConfigurationManifestPaidEventPolicyMapper.CreateTenantCandidate(
                    tenant.PlannedTenantId,
                    tenant.PaidEventPolicy);
            PaidEventPolicyRules.ValidateTenantPolicy(
                effectiveInstancePolicy,
                candidate);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                tenant.ManifestIndex,
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy,
                ConfigurationManifestApplicationFailureCodes.PaidPolicyBroadening,
                "The tenant paid-event policy must remain within the active instance ceiling."));
        }
    }

    private sealed record PaidEventPolicyAuthorityResolution(
        ConfigurationManifestApplyPlan BoundPlan,
        PaidEventPolicyVersion? EffectiveInstancePolicy);

    private async Task<Dictionary<string, bool>> ReadSettingLocksAsync(
        IReadOnlyCollection<ConfigurationManifestPreflightTenant> candidates,
        CancellationToken cancellationToken)
    {
        string[] keys = candidates
            .SelectMany(candidate => candidate.Plan.GuardedSettings
                .Concat(candidate.Plan.UnguardedSettings))
            .Select(setting => setting.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, bool>(keys.Length, StringComparer.Ordinal);
        foreach (string key in keys)
        {
            result[key] = await systemSettingRepository.IsLocked(key, cancellationToken);
        }

        return result;
    }

    private static void AddLockedSettingErrors(
        ConfigurationManifestTenantPlan tenant,
        IReadOnlyDictionary<string, bool> locks,
        ICollection<ConfigurationManifestPreflightError> errors)
    {
        foreach (ConfigurationManifestSettingWrite setting in tenant.GuardedSettings)
        {
            if (locks[setting.Key])
            {
                errors.Add(new ConfigurationManifestPreflightError(
                    tenant.ManifestIndex,
                    setting.Key,
                    PublicationPolicyMutationFailureCodes.LockedPolicy,
                    PublicationPolicyMutationMessages.LockedPolicy));
            }
        }

        foreach (ConfigurationManifestSettingWrite setting in tenant.UnguardedSettings)
        {
            if (locks[setting.Key])
            {
                errors.Add(new ConfigurationManifestPreflightError(
                    tenant.ManifestIndex,
                    setting.Key,
                    ConfigurationManifestApplicationFailureCodes.SettingLocked,
                    "A locked tenant setting cannot be supplied by the manifest."));
            }
        }
    }

    private void AddBrandingErrors(
        ConfigurationManifestTenantPlan tenant,
        TenantBrandingSettingsDocumentLockState lockState,
        ICollection<ConfigurationManifestPreflightError> errors)
    {
        var baseline = new BrandingSettings { DisplayName = tenant.DisplayName };
        BrandingSettings requested = JsonSerializer.Deserialize<BrandingSettings>(
            tenant.BrandingDocument.PayloadJson,
            SerializerOptions) ?? new BrandingSettings();
        IReadOnlyList<string> lockedChanges = brandingLockService.ValidateAllowedChanges(
            baseline,
            requested,
            lockState);
        if (lockedChanges.Count != 0)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                tenant.ManifestIndex,
                SettingsDocumentKeys.Tenant.Branding,
                ConfigurationManifestApplicationFailureCodes.DocumentLocked,
                "The manifest branding document changes an instance-governed field."));
        }
    }

    private async Task AddInstancePublicationPolicyErrorsAsync(
        ConfigurationManifestInstancePlan instance,
        ICollection<ConfigurationManifestPreflightError> errors,
        CancellationToken cancellationToken)
    {
        if (instance.GuardedSettings.IsEmpty)
        {
            return;
        }

        PublicationPolicyMutationSnapshot snapshot =
            await policyStore.ReadInstanceSnapshotAsync(cancellationToken);
        PublicationPolicyCompilationResult compilation =
            PublicationPolicyProposedStateCompiler.CompileInstance(
                new PublicationPolicyInstanceCompilationInput(
                    snapshot.SystemValues,
                    snapshot.TenantValues,
                    ToInstancePublicationMutations(
                        instance.GuardedSettings)));
        if (!compilation.Success
            || compilation.BaseTenantState
                is not ReportingIntakePolicyState baseState)
        {
            AddInvalidPublicationPolicyError(
                manifestIndex: -1,
                instance.GuardedSettings[0].Key,
                errors);
            return;
        }

        ReportingIntakePolicyEvaluation evaluation =
            ReportingIntakePolicyEvaluator.Evaluate(baseState);
        if (!evaluation.Allowed)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                ManifestIndex: -1,
                instance.GuardedSettings[0].Key,
                evaluation.ReasonCode,
                evaluation.Message));
            return;
        }

        PublicationPolicyCompiledTenantState? invalidTenant =
            compilation.TenantStates.FirstOrDefault(state =>
                !ReportingIntakePolicyEvaluator.Evaluate(state.State).Allowed);
        if (invalidTenant is not null)
        {
            evaluation =
                ReportingIntakePolicyEvaluator.Evaluate(invalidTenant.State);
            errors.Add(new ConfigurationManifestPreflightError(
                ManifestIndex: -1,
                instance.GuardedSettings[0].Key,
                evaluation.ReasonCode,
                evaluation.Message));
        }
    }

    private async Task AddPublicationPolicyErrorsAsync(
        ConfigurationManifestInstancePlan instance,
        ConfigurationManifestTenantPlan tenant,
        ICollection<ConfigurationManifestPreflightError> errors,
        CancellationToken cancellationToken)
    {
        if (tenant.GuardedSettings.IsEmpty)
        {
            return;
        }

        PublicationPolicyMutationSnapshot snapshot =
            await policyStore.ReadTenantSnapshotAsync(
                tenant.PlannedTenantId,
                cancellationToken);
        if (!TryApplyInstanceSettings(
                snapshot.SystemValues,
                instance.GuardedSettings,
                out ImmutableArray<PublicationPolicySystemValueSnapshot>
                    effectiveSystemValues))
        {
            AddInvalidPublicationPolicyError(
                tenant.ManifestIndex,
                tenant.GuardedSettings[0].Key,
                errors);
            return;
        }

        ImmutableArray<PublicationPolicySettingMutation> mutations =
            tenant.GuardedSettings
                .Select(setting => new PublicationPolicySettingMutation(
                    setting.Key,
                    PublicationPolicyMutationKind.Set,
                    setting.JsonValue,
                    tenant.PlannedTenantId,
                    IsLocked: null))
                .ToImmutableArray();
        PublicationPolicyCompilationResult compilation =
            PublicationPolicyProposedStateCompiler.CompileTenant(
                new PublicationPolicyTenantCompilationInput(
                    tenant.PlannedTenantId,
                    effectiveSystemValues,
                    snapshot.TenantValues,
                    mutations));
        PublicationPolicyCompiledTenantState? compiled =
            compilation.TenantStates.SingleOrDefault(state =>
                state.TenantId == tenant.PlannedTenantId);
        if (!compilation.Success || compiled is null)
        {
            AddInvalidPublicationPolicyError(
                tenant.ManifestIndex,
                tenant.GuardedSettings[0].Key,
                errors);
            return;
        }

        ReportingIntakePolicyEvaluation evaluation =
            ReportingIntakePolicyEvaluator.Evaluate(compiled.State);
        if (!evaluation.Allowed)
        {
            errors.Add(new ConfigurationManifestPreflightError(
                tenant.ManifestIndex,
                tenant.GuardedSettings[0].Key,
                evaluation.ReasonCode,
                evaluation.Message));
        }
    }

    private static ImmutableArray<PublicationPolicySettingMutation>
        ToInstancePublicationMutations(
            ImmutableArray<ConfigurationManifestSettingWrite> settings) =>
        settings.Select(setting => new PublicationPolicySettingMutation(
                setting.Key,
                PublicationPolicyMutationKind.Set,
                setting.JsonValue,
                TenantId: null,
                IsLocked: false))
            .ToImmutableArray();

    private static bool TryApplyInstanceSettings(
        ImmutableArray<PublicationPolicySystemValueSnapshot> current,
        ImmutableArray<ConfigurationManifestSettingWrite> proposed,
        out ImmutableArray<PublicationPolicySystemValueSnapshot> effective)
    {
        effective = [];
        if (current.IsDefault || proposed.IsDefault)
        {
            return false;
        }

        var byKey =
            new Dictionary<string, PublicationPolicySystemValueSnapshot>(
                StringComparer.Ordinal);
        foreach (PublicationPolicySystemValueSnapshot? snapshot in current)
        {
            if (snapshot is null || !byKey.TryAdd(snapshot.Key, snapshot))
            {
                return false;
            }
        }

        foreach (ConfigurationManifestSettingWrite? setting in proposed)
        {
            if (setting is null)
            {
                return false;
            }

            byKey[setting.Key] = new PublicationPolicySystemValueSnapshot(
                setting.Key,
                setting.JsonValue,
                IsLocked: false);
        }

        effective = byKey.Values
            .OrderBy(snapshot => snapshot.Key, StringComparer.Ordinal)
            .ToImmutableArray();
        return true;
    }

    private static void AddInvalidPublicationPolicyError(
        int manifestIndex,
        string key,
        ICollection<ConfigurationManifestPreflightError> errors) =>
        errors.Add(new ConfigurationManifestPreflightError(
            manifestIndex,
            key,
            PublicationPolicyMutationFailureCodes.InvalidPolicy,
            PublicationPolicyMutationMessages.InvalidPolicy));
}
