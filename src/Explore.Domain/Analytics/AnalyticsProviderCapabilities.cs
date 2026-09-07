namespace Explore.Domain.Analytics;

using Explore.Domain.Enums;

public sealed record AnalyticsProviderCapabilities
{
    public bool SupportsCookielessMode { get; init; }
    public bool SupportsNativeConsentTransition { get; init; }
    public bool SupportsPersonProfiles { get; init; }
    public bool RequiresClientApiKey { get; init; }
    public bool InherentlyCookieless { get; init; }

    public static AnalyticsProviderCapabilities For(AnalyticsProviderEnum provider) => provider switch
    {
        AnalyticsProviderEnum.Posthog => new()
        {
            SupportsCookielessMode = true,
            SupportsNativeConsentTransition = true,
            SupportsPersonProfiles = true,
            RequiresClientApiKey = true,
            InherentlyCookieless = false
        },
        AnalyticsProviderEnum.Plausible => new()
        {
            SupportsCookielessMode = false,
            SupportsNativeConsentTransition = false,
            SupportsPersonProfiles = false,
            RequiresClientApiKey = false,
            InherentlyCookieless = true
        },
        AnalyticsProviderEnum.Rybbit => new()
        {
            SupportsCookielessMode = false,
            SupportsNativeConsentTransition = false,
            SupportsPersonProfiles = false,
            RequiresClientApiKey = false,
            InherentlyCookieless = true
        },
        AnalyticsProviderEnum.RudderStack => new()
        {
            SupportsCookielessMode = false,
            SupportsNativeConsentTransition = false,
            SupportsPersonProfiles = false,
            RequiresClientApiKey = true,
            InherentlyCookieless = false
        },
        _ => new()
        {
            SupportsCookielessMode = false,
            SupportsNativeConsentTransition = false,
            SupportsPersonProfiles = false,
            RequiresClientApiKey = false,
            InherentlyCookieless = true
        }
    };
}
