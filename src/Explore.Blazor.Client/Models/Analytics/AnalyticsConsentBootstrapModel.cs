namespace Explore.Blazor.Client.Models.Analytics;

public class AnalyticsConsentBootstrapModel
{
    public bool CookieBannerEnabled { get; set; }
    public bool CanRunBeforeConsent { get; set; }
    public string DeclineBehavior { get; set; } = "disable";
    public string ConsentCookieKey { get; set; } = "explore_cc_default";
    public int ConsentCookieLifetimeDays { get; set; } = 180;
    public string AnalyticsProvider { get; set; } = "none";
    public PosthogClientBootstrapModel? Posthog { get; set; }
}

public class PosthogClientBootstrapModel
{
    public string CookielessMode { get; set; } = "off";
    public string PersonProfiles { get; set; } = "identified_only";
    public bool SessionReplay { get; set; }
    public bool Autocapture { get; set; }
    public bool Heatmaps { get; set; }
    public bool Toolbar { get; set; }
}
