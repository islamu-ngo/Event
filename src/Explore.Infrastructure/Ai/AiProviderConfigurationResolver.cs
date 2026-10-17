using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Ai;

public sealed class AiProviderConfigurationResolver(
    IOptions<AiProviderSettings> options,
    IHierarchicalSettingsReader settings,
    ITenantContext tenant,
    ISecretResolver secrets,
    AiProviderSettingsValidator validator) : IAiProviderCapabilityResolver
{
    public async Task<AiProviderCapability> ResolveAsync(
        AiAssistantSettingGroup group, CancellationToken cancellationToken = default)
    {
        var result = await ResolveSelectedAsync(
            FromGroup(group, group.ModelId ?? ""), group.TenantOwnedTransport, cancellationToken);
        return new(result.Configuration is not null, result.FailureCode);
    }

    internal async Task<AiProviderConfigurationResolution> ResolveAsync(
        AiChatPayload? request, CancellationToken cancellationToken)
    {
        if (request?.ProviderConfiguration is not { } configuration)
            return await ResolveInstanceAsync(options.Value, cancellationToken);

        var group = await settings.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext { TenantId = tenant.TenantId }, cancellationToken);
        if (configuration.Provider != AiProviderDefaults.ProviderNameToId(group.Provider)
            || configuration.EndpointUrl.Trim() != (group.EndpointUrl ?? "").Trim()
            || configuration.CredentialScope != group.CredentialScope)
            return new(null, "provider_configuration_changed");

        return await ResolveSelectedAsync(
            FromGroup(group, configuration.ModelId.Trim()), group.TenantOwnedTransport, cancellationToken);
    }

    internal async Task<AiProviderConfigurationResolution> ResolveDiscoveryAsync(
        AiAssistantModelDiscoveryRequestDto request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Scope) || !Enum.IsDefined(request.CredentialScope)
            || !Uri.TryCreate(request.EndpointUrl.Trim(), UriKind.Absolute, out var endpoint))
            return new(null, "invalid_settings");

        bool instance = request.Scope == AiModelDiscoveryScope.Instance;
        var defaults = options.Value;
        if (instance && (defaults.Provider != AiProviderSettings.ProviderOpenAiCompatible
            || !defaults.Enabled || request.CredentialScope != defaults.CredentialScope
            || endpoint.AbsoluteUri.TrimEnd('/') != AiTransportAuthority.ResolveInstanceEndpoint(defaults).AbsoluteUri.TrimEnd('/')))
            return new(null, "invalid_settings");
        if (!instance && request.CredentialScope == AiCredentialScope.Instance)
            return new(null, "credential_scope_invalid");

        var selected = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = endpoint.AbsoluteUri,
            CredentialScope = request.CredentialScope,
            ModelId = "model-discovery",
            TimeoutSeconds = Math.Clamp(defaults.TimeoutSeconds, 1, 30),
            AllowLocalProviderEndpoints = instance && defaults.AllowLocalProviderEndpoints
        };
        return await ResolveSelectedAsync(selected, tenantOwnedTransport: !instance, cancellationToken);
    }

    private AiProviderSettings FromGroup(AiAssistantSettingGroup group, string modelId) => new()
    {
        Enabled = group.Enabled,
        Provider = AiProviderDefaults.ProviderNameToId(group.Provider),
        EndpointUrl = group.EndpointUrl?.Trim() ?? "",
        CredentialScope = group.CredentialScope,
        ModelId = modelId,
        MaxInputTokens = group.MaxInputTokens,
        MaxOutputTokens = group.MaxOutputTokens,
        Temperature = group.Temperature,
        TimeoutSeconds = group.TimeoutSeconds,
        RetentionDays = group.RetentionDays,
        DailyMessageLimit = group.DailyMessageLimit,
        ToolProposalsEnabled = group.ToolProposalsEnabled,
        StreamingEnabled = group.StreamingEnabled,
        AllowLocalProviderEndpoints = !group.TenantOwnedTransport && options.Value.AllowLocalProviderEndpoints
    };

    internal Task<AiProviderConfigurationResolution> ResolveInstanceAsync(
        AiProviderSettings selected, CancellationToken cancellationToken) =>
        selected.CredentialScope == AiCredentialScope.Tenant
            ? Task.FromResult(new AiProviderConfigurationResolution(null, "credential_scope_invalid"))
            : ResolveSelectedAsync(selected, tenantOwnedTransport: false, cancellationToken);

    private async Task<AiProviderConfigurationResolution> ResolveSelectedAsync(
        AiProviderSettings selected, bool tenantOwnedTransport, CancellationToken cancellationToken)
    {
        if (!selected.Enabled)
            return new(null, "provider_not_configured");
        if (!validator.Validate(null, selected).Succeeded)
            return new(null, "invalid_settings");
        if ((tenantOwnedTransport || selected.CredentialScope == AiCredentialScope.Tenant)
            && await settings.ResolveAsync<bool>(GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
                new SettingContext(), cancellationToken))
            return new(null, "credential_scope_invalid");

        bool instanceTransport = !tenantOwnedTransport
            && selected.Provider != AiProviderSettings.ProviderFake
            && options.Value.Provider == selected.Provider
            && AiTransportAuthority.HasSameOrigin(AiTransportAuthority.ResolveInstanceEndpoint(selected),
                AiTransportAuthority.ResolveInstanceEndpoint(options.Value));
        if (selected.Provider == AiProviderSettings.ProviderAzureOpenAi
            && string.Equals(selected.AzureCredentialMode, AiProviderSettings.AzureCredentialModeDefaultAzureCredential,
                StringComparison.OrdinalIgnoreCase))
            return selected.CredentialScope == AiCredentialScope.None && !tenantOwnedTransport
                ? new(new ResolvedAiProviderConfiguration(selected, "", instanceTransport: true), null)
                : new(null, "credential_scope_invalid");

        string provider = AiProviderDefaults.ProviderIdToLabel(selected.Provider);
        if (!AiCredentialReference.TryCreate(provider, selected.CredentialScope, tenant.TenantId,
            tenantOwnedTransport, out var reference))
            return new(null, "credential_scope_invalid");
        if (reference is null)
            return new(new ResolvedAiProviderConfiguration(selected, "", instanceTransport), null);

        var resolved = reference.Scope == SecretScope.Instance && reference.Qualifier.Length == 0
            ? await secrets.ResolveAsync(reference.SettingKey, null, cancellationToken)
            : await secrets.ResolveQualifiedAsync(reference.SettingKey, reference.Scope, reference.ScopeId,
                reference.Qualifier, cancellationToken);
        if (!resolved.IsResolved)
            return new(null, resolved.Status switch
            {
                SecretResolutionStatus.Unconfigured => "credential_unconfigured",
                SecretResolutionStatus.Unavailable => "credential_unavailable",
                SecretResolutionStatus.Unauthorized => "credential_unauthorized",
                _ => "credential_invalid"
            });
        if (resolved.Secret?.SettingKey != reference.SettingKey || resolved.Scope != reference.Scope
            || resolved.ScopeId != reference.ScopeId || string.IsNullOrWhiteSpace(resolved.Value))
            return new(null, "credential_invalid");
        return new(new ResolvedAiProviderConfiguration(selected, resolved.Value, instanceTransport), null);
    }
}

internal sealed class ResolvedAiProviderConfiguration(AiProviderSettings settings, string apiKey, bool instanceTransport)
{
    internal AiProviderSettings Settings { get; } = settings;
    internal string ApiKey { get; } = apiKey;
    internal bool InstanceTransport { get; } = instanceTransport;
}

internal sealed record AiProviderConfigurationResolution(ResolvedAiProviderConfiguration? Configuration, string? FailureCode);
