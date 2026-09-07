namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Enums.Analytics;

/// <summary>
/// Strongly-typed group for analytics/tracking settings.
/// </summary>
public class AnalyticsSettingGroup : ISettingGroup
{
    public string Provider { get; private set; } = "none";
    public string ConsentMode { get; private set; } = "pseudonymous";
    public string TransportMode { get; private set; } = "direct";
    public string? EndpointUrl { get; private set; }
    public string? ApiKey { get; private set; }
    public bool Enabled { get; private set; }

    // Cookie consent & storage governance
    public bool CookieConsentEnabled { get; private set; }
    public DeclineBehavior DeclineBehavior { get; private set; } = DeclineBehavior.Cookieless;
    public int ConsentCookieLifetimeDays { get; private set; } = 180;
    public bool GlobalDisableClientTracking { get; private set; }

    // PostHog privacy & feature controls
    public PosthogCookielessMode PosthogCookielessMode { get; private set; } = PosthogCookielessMode.OnReject;
    public PosthogPersonProfiles PosthogPersonProfiles { get; private set; } = PosthogPersonProfiles.IdentifiedOnly;
    public bool PosthogSessionReplay { get; private set; }
    public bool PosthogAutocapture { get; private set; }
    public bool PosthogHeatmaps { get; private set; }
    public bool PosthogToolbar { get; private set; }

    /// <summary>Parsed provider enum with safe fallback.</summary>
    public AnalyticsProviderEnum ProviderEnum => Enum.TryParse<AnalyticsProviderEnum>(Provider, ignoreCase: true, out var p) ? p : AnalyticsProviderEnum.None;

    /// <summary>Stable tenant key for consent cookie scoping (first 8 hex chars of tenant GUID), populated externally.</summary>
    public string? TenantStableKey { get; set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Analytics.Provider,
        GovernanceSettingKeys.Analytics.ConsentMode,
        GovernanceSettingKeys.Analytics.TransportMode,
        GovernanceSettingKeys.Analytics.EndpointUrl,
        GovernanceSettingKeys.Analytics.ApiKey,
        GovernanceSettingKeys.Analytics.Enabled,
        GovernanceSettingKeys.Analytics.CookieConsentEnabled,
        GovernanceSettingKeys.Analytics.DeclineBehavior,
        GovernanceSettingKeys.Analytics.ConsentCookieLifetimeDays,
        GovernanceSettingKeys.Analytics.GlobalDisableClientTracking,
        GovernanceSettingKeys.Analytics.PosthogCookielessMode,
        GovernanceSettingKeys.Analytics.PosthogPersonProfiles,
        GovernanceSettingKeys.Analytics.PosthogSessionReplay,
        GovernanceSettingKeys.Analytics.PosthogAutocapture,
        GovernanceSettingKeys.Analytics.PosthogHeatmaps,
        GovernanceSettingKeys.Analytics.PosthogToolbar
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.Provider, out var provider))
            Provider = SettingValueSerializer.Deserialize(provider.Value, "none");
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.ConsentMode, out var consentMode))
            ConsentMode = SettingValueSerializer.Deserialize(consentMode.Value, "pseudonymous");
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.TransportMode, out var transportMode))
            TransportMode = SettingValueSerializer.Deserialize(transportMode.Value, "direct");
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.EndpointUrl, out var endpointUrl))
            EndpointUrl = SettingValueSerializer.DeserializeString(endpointUrl.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.ApiKey, out var apiKey))
            ApiKey = SettingValueSerializer.DeserializeString(apiKey.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.Enabled, out var enabled))
            Enabled = SettingValueSerializer.Deserialize(enabled.Value, false);

        // Cookie consent & storage governance
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.CookieConsentEnabled, out var cookieConsent))
            CookieConsentEnabled = SettingValueSerializer.Deserialize(cookieConsent.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.DeclineBehavior, out var decline))
            DeclineBehavior = ParseEnum(SettingValueSerializer.DeserializeString(decline.Value), DeclineBehavior.Cookieless);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.ConsentCookieLifetimeDays, out var lifetime))
            ConsentCookieLifetimeDays = SettingValueSerializer.DeserializeInt(lifetime.Value, 180);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.GlobalDisableClientTracking, out var killSwitch))
            GlobalDisableClientTracking = SettingValueSerializer.Deserialize(killSwitch.Value, false);

        // PostHog privacy & feature controls
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogCookielessMode, out var cookieless))
            PosthogCookielessMode = ParsePosthogCookielessMode(SettingValueSerializer.DeserializeString(cookieless.Value));
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogPersonProfiles, out var profiles))
            PosthogPersonProfiles = ParseEnum(SettingValueSerializer.DeserializeString(profiles.Value), PosthogPersonProfiles.IdentifiedOnly);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogSessionReplay, out var replay))
            PosthogSessionReplay = SettingValueSerializer.Deserialize(replay.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogAutocapture, out var autocapture))
            PosthogAutocapture = SettingValueSerializer.Deserialize(autocapture.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogHeatmaps, out var heatmaps))
            PosthogHeatmaps = SettingValueSerializer.Deserialize(heatmaps.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Analytics.PosthogToolbar, out var toolbar))
            PosthogToolbar = SettingValueSerializer.Deserialize(toolbar.Value, false);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum defaultValue) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        // Handle snake_case → PascalCase: "on_reject" → "OnReject", "identified_only" → "IdentifiedOnly"
        var normalized = string.Join("", value.Split('_').Select(part =>
            part.Length > 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part));

        return Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var result) ? result : defaultValue;
    }

    private static PosthogCookielessMode ParsePosthogCookielessMode(string? value) => value?.ToLowerInvariant() switch
    {
        "always" => PosthogCookielessMode.Always,
        "on_reject" => PosthogCookielessMode.OnReject,
        "off" => PosthogCookielessMode.Off,
        _ => PosthogCookielessMode.OnReject
    };
}
