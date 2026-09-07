namespace Explore.Application.DTOs.Onboarding;

/// <summary>
/// Public bootstrap payload for browser-side analytics consent and initialization.
/// Contains only computed, effective runtime configuration — no admin-facing governance inputs.
/// </summary>
public sealed record AnalyticsConsentBootstrapDto
{
    // Consent UX
    public bool CookieBannerEnabled { get; init; }
    public bool CanRunBeforeConsent { get; init; }
    public string DeclineBehavior { get; init; } = "disable";
    public string ConsentCookieKey { get; init; } = "explore_cc_default";
    public int ConsentCookieLifetimeDays { get; init; } = 180;

    // Provider runtime config (public keys only)
    public string AnalyticsProvider { get; init; } = "none";
    public PosthogClientBootstrapDto? Posthog { get; set; }
}

/// <summary>
/// PostHog-specific client bootstrap options.
/// Enum values are mapped to JS string literals at this DTO boundary.
/// </summary>
public sealed record PosthogClientBootstrapDto
{
    public string CookielessMode { get; init; } = "off";
    public string PersonProfiles { get; init; } = "identified_only";
    public bool SessionReplay { get; init; }
    public bool Autocapture { get; init; }
    public bool Heatmaps { get; init; }
    public bool Toolbar { get; init; }
}
