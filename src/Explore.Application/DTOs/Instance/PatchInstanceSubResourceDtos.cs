using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Instance;

public sealed record PatchModuleSettingsDto
{
    public OptionalUpdate<bool> EnableIslamicModule { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> EnableTechModule { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => EnableIslamicModule.HasValue || EnableTechModule.HasValue;
}

public sealed record PatchEventPolicyDto
{
    public OptionalUpdate<bool> AllowUserSubmittedEvents { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowOrganizationSubmittedEvents { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowGroupSubmittedEvents { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> EventCardClickOpensDetailPage { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantEventCardClickBehavior { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => AllowUserSubmittedEvents.HasValue || AllowOrganizationSubmittedEvents.HasValue
        || AllowGroupSubmittedEvents.HasValue || EventCardClickOpensDetailPage.HasValue || LockTenantEventCardClickBehavior.HasValue;
}

public sealed record PatchOrganizationPolicyDto
{
    public OptionalUpdate<bool> RequireOrganizationVerification { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowTenantToOmitVerification { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowOrganizationSelfRegistration { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowGroupSelfRegistration { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => RequireOrganizationVerification.HasValue || AllowTenantToOmitVerification.HasValue
        || AllowOrganizationSelfRegistration.HasValue || AllowGroupSelfRegistration.HasValue;
}

public sealed record PatchBrandingSettingsDto
{
    public OptionalUpdate<string?> DefaultBrandDisplayName { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> DefaultBrandLogoUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> DefaultBrandFaviconUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> DefaultBrandCustomCssUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> LockTenantBrandDisplayName { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantBrandLogoUrl { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantBrandFaviconUrl { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantBrandCustomCssUrl { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => DefaultBrandDisplayName.HasValue || DefaultBrandLogoUrl.HasValue || DefaultBrandFaviconUrl.HasValue
        || DefaultBrandCustomCssUrl.HasValue || LockTenantBrandDisplayName.HasValue || LockTenantBrandLogoUrl.HasValue
        || LockTenantBrandFaviconUrl.HasValue || LockTenantBrandCustomCssUrl.HasValue;
}

public sealed record PatchDomainSettingsDto
{
    public OptionalUpdate<string?> InstanceBaseDomain { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> AdminHost { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> AllowTenantCustomDomains { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantSubdomain { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantCustomDomain { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => InstanceBaseDomain.HasValue || AdminHost.HasValue || AllowTenantCustomDomains.HasValue
        || LockTenantSubdomain.HasValue || LockTenantCustomDomain.HasValue;
}

public sealed record PatchTenantDelegationSettingsDto
{
    public OptionalUpdate<bool> AllowTenantSelfServiceRegistration { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowTenantWhiteLabeling { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> DefaultPublicHomePage { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> LockTenantHomePagePreference { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantSmtp { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantStorage { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantAnalytics { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantAiAssistant { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => AllowTenantSelfServiceRegistration.HasValue || AllowTenantWhiteLabeling.HasValue
        || DefaultPublicHomePage.HasValue || LockTenantHomePagePreference.HasValue || LockTenantSmtp.HasValue
        || LockTenantStorage.HasValue || LockTenantAnalytics.HasValue || LockTenantAiAssistant.HasValue;
}

public sealed record PatchAdminPortalSettingsDto
{
    public OptionalUpdate<bool> Enabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> PublicUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> AllowTenantAdminAccess { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => Enabled.HasValue || PublicUrl.HasValue || AllowTenantAdminAccess.HasValue;
}

public sealed record PatchAiAssistantGovernanceSettingsDto
{
    public OptionalUpdate<bool> Enabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<AiAssistantProviderConfigurationWriteDto> ProviderConfiguration { get; init; } = OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Unspecified();
    public OptionalUpdate<bool> AllowAnonymousAccess { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> ToolProposalsEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantAiAssistant { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => Enabled.HasValue || ProviderConfiguration.HasValue || AllowAnonymousAccess.HasValue
        || ToolProposalsEnabled.HasValue || LockTenantAiAssistant.HasValue;
}

public sealed record AiAssistantProviderConfigurationWriteDto
{
    public string Provider { get; init; } = string.Empty;
    public string EndpointUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedModelIds { get; init; } = [];
}

public sealed record PatchMcpGovernanceSettingsDto
{
    public OptionalUpdate<bool> Enabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> EnableLegacySse { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantMcp { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantMcpLegacySse { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => Enabled.HasValue || EnableLegacySse.HasValue || LockTenantMcp.HasValue || LockTenantMcpLegacySse.HasValue;
}

public sealed record PatchRenderPolicySettingsDto
{
    public OptionalUpdate<string?> RenderPolicyPreset { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> EnableAdvancedRenderPolicyOverrides { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> GlobalRenderMode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> GlobalPrerenderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> PublicSeoRenderMode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> PublicSeoPrerenderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> OperationalRenderMode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> OperationalPrerenderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> AdminRenderMode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> AdminPrerenderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> OnboardingRenderMode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> OnboardingPrerenderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> AllowTenantRenderPolicyOverride { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantPublicSeoRenderPolicy { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantOperationalRenderPolicy { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantAdminRenderPolicy { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => RenderPolicyPreset.HasValue || EnableAdvancedRenderPolicyOverrides.HasValue
        || GlobalRenderMode.HasValue || GlobalPrerenderEnabled.HasValue || PublicSeoRenderMode.HasValue
        || PublicSeoPrerenderEnabled.HasValue || OperationalRenderMode.HasValue || OperationalPrerenderEnabled.HasValue
        || AdminRenderMode.HasValue || AdminPrerenderEnabled.HasValue || OnboardingRenderMode.HasValue
        || OnboardingPrerenderEnabled.HasValue || AllowTenantRenderPolicyOverride.HasValue
        || LockTenantPublicSeoRenderPolicy.HasValue || LockTenantOperationalRenderPolicy.HasValue || LockTenantAdminRenderPolicy.HasValue;
}
