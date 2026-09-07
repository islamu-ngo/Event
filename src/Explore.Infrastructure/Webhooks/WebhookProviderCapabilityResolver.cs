using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookProviderCapabilityResolver(IOptionsMonitor<WebhookOptions> options)
    : IWebhookProviderCapabilityResolver
{
    public const string LocalResolutionVersion = "event-local-v1";

    public static readonly WebhookProviderCapability LocalCapabilities =
        WebhookProviderCapability.EndpointManagement |
        WebhookProviderCapability.EventCatalog;

    public WebhookProviderModeCapabilityResolution Resolve(WebhookProviderMode providerMode)
    {
        if (!Enum.IsDefined(providerMode))
        {
            return Unavailable(providerMode, "webhook_provider_mode_invalid");
        }

        var current = options.CurrentValue;
        if (current.IsDisabled)
        {
            return providerMode == WebhookProviderMode.Disabled
                ? Available(providerMode, WebhookProviderCapability.None, WebhookProviderCapability.None, "disabled-v1")
                : Unavailable(providerMode, "webhook_provider_disabled");
        }

        if (!current.AllowTenantOverride && !MatchesConfiguredMode(providerMode, current))
        {
            return Unavailable(providerMode, "webhook_provider_tenant_override_disabled");
        }

        return providerMode switch
        {
            WebhookProviderMode.Disabled =>
                Available(providerMode, WebhookProviderCapability.None, WebhookProviderCapability.None, "disabled-v1"),
            WebhookProviderMode.DryRun =>
                Available(providerMode, WebhookProviderCapability.None, WebhookProviderCapability.None, "dry-run-v1"),
            WebhookProviderMode.Local =>
                Available(providerMode, LocalCapabilities, WebhookProviderCapability.None, LocalResolutionVersion),
            WebhookProviderMode.Svix => ResolveSvix(providerMode, includeLocal: false, current),
            WebhookProviderMode.Composite => ResolveSvix(providerMode, includeLocal: true, current),
            _ => Unavailable(providerMode, "webhook_provider_mode_invalid")
        };
    }

    private static WebhookProviderModeCapabilityResolution ResolveSvix(
        WebhookProviderMode providerMode,
        bool includeLocal,
        WebhookOptions current)
    {
        var baseUrlConfigured = !string.IsNullOrWhiteSpace(current.Svix.BaseUrl);
        if (!baseUrlConfigured ||
            !SvixConformanceProfileRegistry.TryResolve(
                current.Svix.Environment,
                current.Svix.ProviderVersion,
                current.Svix.CapabilityPolicyVersion,
                baseUrlConfigured,
                out var profile) ||
            profile is not { IsVerified: true, DeploymentKind: SvixDeploymentKind.SelfHosted })
        {
            return Unavailable(providerMode, "svix_self_hosted_profile_unsupported");
        }

        return Available(
            providerMode,
            includeLocal ? LocalCapabilities : WebhookProviderCapability.None,
            profile.Capabilities,
            profile.CapabilityPolicyVersion,
            profile.Environment,
            profile.ProviderVersion);
    }

    private static bool MatchesConfiguredMode(WebhookProviderMode providerMode, WebhookOptions current) =>
        providerMode switch
        {
            WebhookProviderMode.Disabled => current.IsProvider(WebhookOptions.ProviderDisabled),
            WebhookProviderMode.Local => current.IsProvider(WebhookOptions.ProviderLocal),
            WebhookProviderMode.Svix => current.IsProvider(WebhookOptions.ProviderSvix),
            WebhookProviderMode.Composite => current.IsProvider(WebhookOptions.ProviderComposite),
            WebhookProviderMode.DryRun => current.IsProvider(WebhookOptions.ProviderDryRun),
            _ => false
        };

    private static WebhookProviderModeCapabilityResolution Available(
        WebhookProviderMode providerMode,
        WebhookProviderCapability localCapabilities,
        WebhookProviderCapability providerCapabilities,
        string resolutionVersion,
        string? providerEnvironment = null,
        string? providerVersion = null) =>
        new(
            providerMode,
            true,
            localCapabilities,
            providerCapabilities,
            providerEnvironment,
            providerVersion,
            resolutionVersion,
            null);

    private static WebhookProviderModeCapabilityResolution Unavailable(
        WebhookProviderMode providerMode,
        string reasonCode) =>
        new(
            providerMode,
            false,
            WebhookProviderCapability.None,
            WebhookProviderCapability.None,
            null,
            null,
            "unavailable-v1",
            reasonCode);
}
