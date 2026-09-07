using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookProviderModeCapabilityResolution(
    WebhookProviderMode ProviderMode,
    bool IsProviderModeAvailable,
    WebhookProviderCapability LocalCapabilities,
    WebhookProviderCapability ProviderCapabilities,
    string? ProviderEnvironment,
    string? ProviderVersion,
    string ResolutionVersion,
    string? UnavailableReasonCode)
{
    public bool SupportsLocalConfiguration(WebhookProviderCapability capability) =>
        IsProviderModeAvailable &&
        capability != WebhookProviderCapability.None &&
        (LocalCapabilities & capability) == capability;
}

public interface IWebhookProviderCapabilityResolver
{
    WebhookProviderModeCapabilityResolution Resolve(WebhookProviderMode providerMode);
}
