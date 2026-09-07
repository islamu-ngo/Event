using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.TenantPolicy;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record UpdateTenantPolicyRequest
{
    public bool AllowUserSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSubmittedEvents { get; init; } = true;
    public bool AllowGroupSubmittedEvents { get; init; } = true;
    public bool AllowOrganizationSelfRegistration { get; init; } = true;
    public bool AllowGroupSelfRegistration { get; init; } = true;
    public bool EventCardClickOpensDetailPage { get; init; }
    public bool RequireEventApproval { get; init; }
    public bool RequireOrganizationVerification { get; init; } = true;
    public string PreferredHomePage { get; init; } = "EventList";
    public string Subdomain { get; init; } = string.Empty;
    public string CustomDomain { get; init; } = string.Empty;
    public bool AnnouncementBarEnabled { get; init; }
    public string AnnouncementBarMessage { get; init; } = string.Empty;
    public string AnnouncementBarLinkText { get; init; } = string.Empty;
    public string AnnouncementBarLinkUrl { get; init; } = string.Empty;
    public bool ForceAnnouncementBarRedisplay { get; init; }

    // Community guidelines
    public string CommunityGuidelinesContent { get; init; } = string.Empty;

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
