using Explore.Application.Analytics;
using Explore.Domain.Enums;

namespace Explore.Application.Models;

/// <summary>
/// Analytics connection parameters resolved from SystemSetting/TenantSetting.
/// Instance admin can lock settings (IsLocked) to enforce a SaaS-wide analytics provider,
/// or leave unlocked so tenants can choose their own provider.
/// </summary>
public class AnalyticsConfiguration
{
    /// <summary>The active analytics provider (None, Posthog, Plausible, Rybbit, or RudderStack).</summary>
    public AnalyticsProviderEnum Provider { get; set; } = AnalyticsProviderEnum.None;

    /// <summary>Whether analytics tracking is enabled. When false, all calls are no-ops.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Consent/privacy mode controlling whether analytics is anonymous, pseudonymous, or identified.
    /// </summary>
    public AnalyticsConsentMode ConsentMode { get; set; } = AnalyticsConsentMode.Pseudonymous;

    /// <summary>
    /// Browser transport mode: direct script loading, first-party proxying, or server relay.
    /// </summary>
    public AnalyticsTransportMode TransportMode { get; set; } = AnalyticsTransportMode.Direct;

    /// <summary>
    /// API key / project key for the analytics provider.
    /// For Posthog/Plausible/Rybbit/RudderStack: provider key or site identifier.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Endpoint URL for self-hosted or regional deployments.
    /// For Posthog: e.g., "https://us.i.posthog.com" or self-hosted URL.
    /// For Plausible/Rybbit/RudderStack: self-hosted or cloud endpoint URL.
    /// </summary>
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// Personal API key for feature flag local evaluation (PostHog only).
    /// Not required for basic event tracking.
    /// </summary>
    public string? PersonalApiKey { get; set; }
}
