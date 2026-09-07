using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.PublicExperience;

namespace Explore.Application.DTOs.Onboarding;

public sealed record PublicExperienceSettingsDto
{
    private IReadOnlyList<string> _enabledModules = Array.AsReadOnly(Array.Empty<string>());

    public Guid TenantId { get; init; }
    public bool IsAvailable { get; init; }
    public string? UnavailableCode { get; init; }
    public TenantDirectoryOperatorPublicDto? DirectoryOperator { get; init; }
    public InstanceOperatorPublicDto? InstanceOperator { get; init; }
    public Explore.Application.Models.PublicExperienceMode Mode { get; init; } = Explore.Application.Models.PublicExperienceMode.DiscoveryCentric;
    public string DeploymentMode { get; init; } = "SingleTenant";
    public string PreferredHomePage { get; init; } = "EventList";
    public string BrandDisplayName { get; init; } = string.Empty;
    public string BrandLogoUrl { get; init; } = string.Empty;
    public string BrandFaviconUrl { get; init; } = string.Empty;
    public string BrandCustomCssUrl { get; init; } = string.Empty;
    public string InstanceBaseDomain { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string CustomDomain { get; init; } = string.Empty;
    public bool IsIslamicModuleEnabled { get; init; }
    public bool IsTechModuleEnabled { get; init; }
    public bool AllowUserSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSubmittedEvents { get; init; } = true;
    public bool AllowGroupSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSelfRegistration { get; init; } = true;
    public bool AllowGroupSelfRegistration { get; init; } = true;
    public bool EventCardClickOpensDetailPage { get; init; }
    public bool AnnouncementBarEnabled { get; init; }
    public string AnnouncementBarMessage { get; init; } = string.Empty;
    public string AnnouncementBarLinkText { get; init; } = string.Empty;
    public string AnnouncementBarLinkUrl { get; init; } = string.Empty;
    public int AnnouncementBarRevision { get; init; }
    public string CommunityGuidelinesContent { get; init; } = string.Empty;
    public IReadOnlyList<string> EnabledModules
    {
        get => _enabledModules;
        init => _enabledModules = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public string AnalyticsProvider { get; init; } = "none";
    public bool AnalyticsEnabled { get; init; }
    public string AnalyticsConsentMode { get; init; } = "pseudonymous";
    public string AnalyticsTransportMode { get; init; } = "direct";
    public bool AnalyticsAllowIdentify { get; init; }
    public string AnalyticsPublicApiKey { get; init; } = string.Empty;
    public string AnalyticsEndpointUrl { get; init; } = string.Empty;
    public AnalyticsConsentBootstrapDto? AnalyticsConsent { get; init; }
    public int RenderPolicyVersion { get; init; } = 1;
    public string RenderPolicyPreset { get; init; } = "AllInteractiveServer";
    public bool EnableAdvancedRenderPolicyOverrides { get; init; }
    public string GlobalRenderMode { get; init; } = "InteractiveServer";
    public bool GlobalPrerenderEnabled { get; init; }
    public string PublicSeoRenderMode { get; init; } = "InteractiveServer";
    public bool PublicSeoPrerenderEnabled { get; init; }
    public string OperationalRenderMode { get; init; } = "InteractiveServer";
    public bool OperationalPrerenderEnabled { get; init; }
    public string AdminRenderMode { get; init; } = "InteractiveServer";
    public bool AdminPrerenderEnabled { get; init; }
    public string OnboardingRenderMode { get; init; } = "InteractiveServer";
    public bool OnboardingPrerenderEnabled { get; init; }
    public bool DisallowInteractiveServerOnOnboarding { get; init; } = true;
    public bool IsAiAssistantEnabled { get; init; }
    public bool IsAiAssistantAvailable { get; init; }
    public bool AiAssistantAllowAnonymousAccess { get; init; }
    public FooterConfigDto FooterConfig { get; init; } = new();
    public Guid? DefaultThemeId { get; init; }
    public string ThemeMode { get; init; } = "system";
    public string Direction { get; init; } = "auto";
    public string Language { get; init; } = "en";
    public bool ClientPickerEnabled { get; init; } = true;
}
