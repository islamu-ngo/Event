namespace Explore.Domain.Enums.Analytics;

/// <summary>
/// Reason codes produced by the analytics runtime profile resolver.
/// Used for admin UX diagnostics and supportability.
/// </summary>
public enum ProfileResolveReason
{
    /// <summary>Global kill switch is active — all browser analytics disabled.</summary>
    GlobalKillSwitch = 0,

    /// <summary>Analytics disabled or provider set to None.</summary>
    AnalyticsDisabled = 1,

    /// <summary>Provider is inherently cookieless by design (Plausible, Rybbit).</summary>
    ProviderInherentlyCookieless = 2,

    /// <summary>PostHog always-cookieless mode selected by operator.</summary>
    PosthogCookielessAlways = 3,

    /// <summary>PostHog on-reject cookieless mode — runs before consent, falls back on decline.</summary>
    PosthogCookielessOnReject = 4,

    /// <summary>PostHog cookieless mode off — full consent required before any tracking.</summary>
    PosthogFullConsentRequired = 5,

    /// <summary>Non-PostHog provider requires full consent (e.g., RudderStack).</summary>
    ProviderRequiresFullConsent = 6,

    /// <summary>Operator enabled the cookie consent banner.</summary>
    CookieBannerEnabledByOperator = 7,

    /// <summary>Cookie banner suppressed — not needed for this profile.</summary>
    CookieBannerSuppressed = 8
}
