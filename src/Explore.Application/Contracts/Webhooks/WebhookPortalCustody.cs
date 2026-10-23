using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

/// <summary>Immutable provider configuration evidence, not token or human release authority.</summary>
public sealed record WebhookPortalOptionsCustody(
    bool Enabled, string Provider, bool AppPortalEnabled, string Environment,
    string ProviderVersion, string CapabilityPolicyVersion, string? BaseUrl, string? AuthTokenSecretRef);

/// <summary>Server-resolved owner and provider state retained before entering the provider service.</summary>
public sealed record WebhookPortalConsumerCustody(
    Guid ConsumerId,
    WebhookOwnershipScope Ownership,
    WebhookConsumerStatus Status,
    WebhookProviderMode ProviderMode,
    int ConfigurationVersion)
{
    public static WebhookPortalConsumerCustody From(WebhookConsumer consumer) =>
        new(consumer.Id, consumer.Ownership, consumer.Status, consumer.ProviderMode, consumer.ConfigurationVersion);
}

/// <summary>
/// Retains provider identity, verification generation and governed capability state across remote awaits.
/// Capability resolution does not advance the verification fence, so its complete state is retained separately.
/// </summary>
public sealed record WebhookPortalBindingCustody(
    Guid BindingId,
    Guid? TenantId,
    Guid ConsumerId,
    Guid InstanceId,
    WebhookProviderKind ProviderKind,
    string Environment,
    string ApplicationUid,
    string? ExternalApplicationId,
    string ProviderVersion,
    WebhookProviderCapability Capabilities,
    WebhookProviderCapability GovernanceAllowedCapabilities,
    string CapabilityPolicyVersion,
    DateTimeOffset CapabilitiesResolvedAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    long ConcurrencyVersion,
    long VerificationFence)
{
    public static WebhookPortalBindingCustody From(WebhookConsumerProviderBinding binding) =>
        new(
            binding.Id,
            binding.TenantId,
            binding.WebhookConsumerId,
            binding.InstanceId,
            binding.ProviderKind,
            binding.ProviderEnvironment,
            binding.ApplicationUid,
            binding.ExternalApplicationId,
            binding.ProviderVersion,
            binding.Capabilities,
            binding.GovernanceAllowedCapabilities,
            binding.CapabilityResolutionVersion,
            binding.CapabilitiesResolvedAtUtc,
            binding.VerifiedAtUtc,
            binding.ConcurrencyVersion,
            binding.VerificationFence);
}
