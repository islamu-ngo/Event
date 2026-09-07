using Explore.Domain.Enums.Analytics;

namespace Explore.Application.DTOs.Analytics;

public sealed record AnalyticsGovernanceSettingsDto
{
    private IReadOnlyList<string> _resolveReasons = Array.AsReadOnly(Array.Empty<string>());

    // Provider & basic config (read-only context from environment/secrets)
    public string Provider { get; init; } = "none";
    public bool Enabled { get; init; }
    public string? EndpointUrl { get; init; }
    public bool HasApiKey { get; init; }

    // Cookie consent & storage governance (admin-editable)
    public bool CookieConsentEnabled { get; init; }
    public DeclineBehavior DeclineBehavior { get; init; } = DeclineBehavior.Cookieless;
    public int ConsentCookieLifetimeDays { get; init; } = 180;
    public bool GlobalDisableClientTracking { get; init; }

    // PostHog privacy & feature controls (admin-editable)
    public PosthogCookielessMode PosthogCookielessMode { get; init; } = PosthogCookielessMode.OnReject;
    public PosthogPersonProfiles PosthogPersonProfiles { get; init; } = PosthogPersonProfiles.IdentifiedOnly;
    public bool PosthogSessionReplay { get; init; }
    public bool PosthogAutocapture { get; init; }
    public bool PosthogHeatmaps { get; init; }
    public bool PosthogToolbar { get; init; }

    // Computed advisory info (read-only, computed by resolver)
    public bool CookieBannerRequired { get; init; }
    public bool CanRunBeforeConsent { get; init; }
    public string StorageProfile { get; init; } = "Unknown";
    public IReadOnlyList<string> ResolveReasons
    {
        get => _resolveReasons;
        init => _resolveReasons = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}
