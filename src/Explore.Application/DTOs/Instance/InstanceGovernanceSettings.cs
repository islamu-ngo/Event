namespace Explore.Application.DTOs.Instance;

public sealed record InstanceGovernanceSettings
{
    public required DeploymentModeDto DeploymentMode { get; init; }
    public required ModuleSettingsDto Modules { get; init; }
    public required EventPolicyDto EventPolicy { get; init; }
    public required OrganizationPolicyDto OrganizationPolicy { get; init; }
    public required BrandingSettingsDto Branding { get; init; }
    public required DomainSettingsDto Domains { get; init; }
    public required TenantDelegationSettingsDto TenantDelegation { get; init; }
    public required AdminPortalSettingsDto AdminPortal { get; init; }
    public required AiAssistantGovernanceSettingsDto AiAssistant { get; init; }
    public required McpGovernanceSettingsDto Mcp { get; init; }
    public required RenderPolicySettingsDto RenderPolicy { get; init; }
    public LocationPrivacyGovernanceSettingsDto? LocationPrivacy { get; init; }
}
