namespace Explore.Domain.Settings.Definitions;

public static class AnalyticsSettingDefinitions
{
    public static readonly SettingDefinition Provider = new(
        Key: "analytics.provider",
        ValueType: SettingValueType.String,
        DefaultValue: "\"none\"",
        Category: "Analytics",
        Description: "Analytics provider (none, posthog, plausible, rybbit, rudderstack)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["none", "posthog", "plausible", "rybbit", "rudderstack"]);

    public static readonly SettingDefinition Enabled = new(
        Key: "analytics.enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable analytics tracking",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ConsentMode = new(
        Key: "analytics.consent_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"pseudonymous\"",
        Category: "Analytics",
        Description: "Analytics identity mode (anonymous, pseudonymous, identified)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["anonymous", "pseudonymous", "identified"]);

    public static readonly SettingDefinition TransportMode = new(
        Key: "analytics.transport_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"direct\"",
        Category: "Analytics",
        Description: "Analytics browser transport mode (direct, proxy, relay)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["direct", "proxy", "relay"]);

    public static readonly SettingDefinition ApiKey = new(
        Key: "analytics.api_key",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Analytics",
        Description: "Analytics provider public/write API key",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition EndpointUrl = new(
        Key: "analytics.endpoint_url",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Analytics",
        Description: "Analytics provider endpoint URL (supports self-hosted deployments)",
        MaxScope: SettingScope.Tenant);

    // Cookie consent & storage governance

    public static readonly SettingDefinition CookieConsentEnabled = new(
        Key: "analytics.cookie_consent_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable browser cookie consent banner for analytics",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DeclineBehavior = new(
        Key: "analytics.decline_behavior",
        ValueType: SettingValueType.String,
        DefaultValue: "\"cookieless\"",
        Category: "Analytics",
        Description: "Behavior when user declines cookies (disable, cookieless)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["disable", "cookieless"]);

    public static readonly SettingDefinition ConsentCookieLifetimeDays = new(
        Key: "analytics.consent_cookie_lifetime_days",
        ValueType: SettingValueType.Integer,
        DefaultValue: "180",
        Category: "Analytics",
        Description: "Consent cookie lifetime in days (ICO recommends 6 months)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition GlobalDisableClientTracking = new(
        Key: "analytics.global_disable_client_tracking",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Emergency kill switch — disables all browser analytics immediately across all tenants",
        MaxScope: SettingScope.Instance);

    // PostHog privacy & feature controls

    public static readonly SettingDefinition PosthogCookielessMode = new(
        Key: "analytics.posthog_cookieless_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"on_reject\"",
        Category: "Analytics",
        Description: "PostHog cookieless mode (off, always, on_reject)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["off", "always", "on_reject"]);

    public static readonly SettingDefinition PosthogPersonProfiles = new(
        Key: "analytics.posthog_person_profiles",
        ValueType: SettingValueType.String,
        DefaultValue: "\"identified_only\"",
        Category: "Analytics",
        Description: "PostHog person profiles mode (always, identified_only, never)",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["always", "identified_only", "never"]);

    public static readonly SettingDefinition PosthogSessionReplay = new(
        Key: "analytics.posthog_session_replay",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable PostHog session replay recording",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition PosthogAutocapture = new(
        Key: "analytics.posthog_autocapture",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable PostHog automatic event capture",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition PosthogHeatmaps = new(
        Key: "analytics.posthog_heatmaps",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable PostHog heatmap tracking",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition PosthogToolbar = new(
        Key: "analytics.posthog_toolbar",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Analytics",
        Description: "Enable PostHog toolbar metrics",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Provider, Enabled, ConsentMode, TransportMode, ApiKey, EndpointUrl,
        CookieConsentEnabled, DeclineBehavior, ConsentCookieLifetimeDays, GlobalDisableClientTracking,
        PosthogCookielessMode, PosthogPersonProfiles, PosthogSessionReplay, PosthogAutocapture,
        PosthogHeatmaps, PosthogToolbar
    ];
}
