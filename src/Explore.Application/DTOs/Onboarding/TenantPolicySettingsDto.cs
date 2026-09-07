namespace Explore.Application.DTOs.Onboarding;

public sealed record TenantPolicySettingsDto
{
    public bool AllowUserSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSubmittedEvents { get; init; } = true;
    public bool AllowGroupSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSelfRegistration { get; init; } = true;
    public bool AllowGroupSelfRegistration { get; init; } = true;
    public bool EventCardClickOpensDetailPage { get; init; }
    public bool RequireEventApproval { get; init; }
    public bool RequireOrganizationVerification { get; init; } = true;
    public bool CanTenantOmitVerification { get; init; }
    public bool IsTenantWhiteLabelingEnabled { get; init; }
    public string PreferredHomePage { get; init; } = "EventList";
    public string InstanceBaseDomain { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string CustomDomain { get; init; } = string.Empty;
    public string BrandDisplayName { get; init; } = string.Empty;
    public string BrandLogoUrl { get; init; } = string.Empty;
    public string BrandFaviconUrl { get; init; } = string.Empty;
    public string BrandCustomCssUrl { get; init; } = string.Empty;
    public bool AnnouncementBarEnabled { get; init; }
    public string AnnouncementBarMessage { get; init; } = string.Empty;
    public string AnnouncementBarLinkText { get; init; } = string.Empty;
    public string AnnouncementBarLinkUrl { get; init; } = string.Empty;
    public int AnnouncementBarRevision { get; init; }
    public bool CanOverrideHomePagePreference { get; init; } = true;
    public bool CanOverrideSubdomain { get; init; } = true;
    public bool CanOverrideCustomDomain { get; init; } = true;
    public bool CanOverrideBrandDisplayName { get; init; } = true;
    public bool CanOverrideBrandLogoUrl { get; init; } = true;
    public bool CanOverrideBrandFaviconUrl { get; init; } = true;
    public bool CanOverrideBrandCustomCssUrl { get; init; } = true;
    public bool CanOverrideEventCardClickBehavior { get; init; } = true;

    // Render policy tenant overrides
    public string RenderPolicyPreset { get; init; } = string.Empty;
    public bool EnableAdvancedRenderPolicyOverrides { get; init; }
    public string GlobalRenderMode { get; init; } = string.Empty;
    public bool GlobalPrerenderEnabled { get; init; }
    public string PublicSeoRenderMode { get; init; } = string.Empty;
    public bool PublicSeoPrerenderEnabled { get; init; }
    public string OperationalRenderMode { get; init; } = string.Empty;
    public bool OperationalPrerenderEnabled { get; init; }
    public string AdminRenderMode { get; init; } = string.Empty;
    public bool AdminPrerenderEnabled { get; init; }
    public bool CanOverrideRenderPolicy { get; init; }
    public bool CanOverridePublicSeoRenderPolicy { get; init; }
    public bool CanOverrideOperationalRenderPolicy { get; init; }
    public bool CanOverrideAdminRenderPolicy { get; init; }

    // Community guidelines
    public string CommunityGuidelinesContent { get; init; } = string.Empty;
    public bool CanOverrideCommunityGuidelines { get; init; } = true;

    // Category-level override flags (inverse of instance lock)
    public bool CanOverrideSmtp { get; init; }
    public bool CanOverrideStorage { get; init; }
    public bool CanOverrideAnalytics { get; init; }
    public bool CanOverrideAiAssistant { get; init; }
    public bool CanOverrideMcp { get; init; }
    public bool CanOverrideMcpLegacySse { get; init; }

    // AI assistant integration
    public bool AiAssistantEnabled { get; init; }
    public string AiAssistantProvider { get; init; } = "none";
    public string AiAssistantEndpointUrl { get; init; } = string.Empty;
    public string AiAssistantApiKey { get; init; } = string.Empty;
    public string AiAssistantModelId { get; init; } = string.Empty;
    public IReadOnlyList<string> AiAssistantAllowedModelIds { get; init; } = [];
    public bool AiAssistantAllowAnonymousAccess { get; init; }

    // API-hosted MCP adapter runtime governance
    public bool McpEnabled { get; init; }
    public bool McpEnableLegacySse { get; init; }
}
