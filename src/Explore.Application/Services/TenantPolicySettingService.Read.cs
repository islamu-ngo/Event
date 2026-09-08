using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public partial class TenantPolicySettingService
{
    public async Task<TenantPolicySettingsDto> ReadEffectiveTenantSettingsAsync(Guid tenantId)
    {
        var systemSettings = (await _systemSettingRepository.GetAllSettings())
            .ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal);
        var tenantSettings = (await _tenantSettingRepository.GetAllForTenant(tenantId))
            .ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal);

        SystemSetting? GetSystem(string key) => systemSettings.GetValueOrDefault(key);
        TenantSetting? GetTenant(string key) => tenantSettings.GetValueOrDefault(key);

        var systemUserSubmission = GetSystem(GovernanceSettingKeys.Events.UserSubmissionEnabled);
        var systemOrgSubmission = GetSystem(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled);
        var systemGroupSubmission = GetSystem(GovernanceSettingKeys.Events.GroupSubmissionEnabled);
        var systemRequireApproval = GetSystem(GovernanceSettingKeys.Events.RequireApproval);
        var systemEventCardClickOpensDetailPage = GetSystem(GovernanceSettingKeys.Events.CardClickOpensDetailPage);
        var systemRequireVerification = GetSystem(GovernanceSettingKeys.Organizations.VerificationRequired);
        var systemTenantCanOmitVerification = GetSystem(GovernanceSettingKeys.Organizations.TenantCanOmitVerification);
        var systemOrgSelfRegistration = GetSystem(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled);
        var systemGroupSelfRegistration = GetSystem(GovernanceSettingKeys.Groups.SelfRegistrationEnabled);
        var systemDeploymentMode = GetSystem(GovernanceSettingKeys.Deployment.Mode);
        var systemTenantWhiteLabeling = GetSystem(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled);
        var systemHomePage = GetSystem(GovernanceSettingKeys.Routing.DefaultPublicHomePage);
        var systemInstanceBaseDomain = GetSystem(GovernanceSettingKeys.Domains.InstanceBaseDomain);
        var systemAllowCustomDomain = GetSystem(GovernanceSettingKeys.Domains.AllowTenantCustomDomain);
        var systemTenantSubdomain = GetSystem(GovernanceSettingKeys.Domains.TenantSubdomain);
        var systemTenantCustomDomain = GetSystem(GovernanceSettingKeys.Domains.TenantCustomDomain);
        var systemBrandDisplayName = GetSystem(GovernanceSettingKeys.Branding.DisplayName);
        var systemBrandLogoUrl = GetSystem(GovernanceSettingKeys.Branding.LogoUrl);
        var systemBrandFaviconUrl = GetSystem(GovernanceSettingKeys.Branding.FaviconUrl);
        var systemBrandCustomCssUrl = GetSystem(GovernanceSettingKeys.Branding.CustomCssUrl);
        var systemAllowTenantRenderOverride = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride);
        var systemLockPublicSeo = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo);
        var systemLockOperational = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational);
        var systemLockAdmin = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin);
        var systemLockSmtp = GetSystem(GovernanceSettingKeys.TenantDelegation.LockSmtp);
        var systemLockStorage = GetSystem(GovernanceSettingKeys.TenantDelegation.LockStorage);
        var systemLockAnalytics = GetSystem(GovernanceSettingKeys.TenantDelegation.LockAnalytics);
        var systemLockAiAssistant = GetSystem(GovernanceSettingKeys.TenantDelegation.LockAiAssistant);
        var systemLockMcp = GetSystem(GovernanceSettingKeys.TenantDelegation.LockMcp);
        var systemLockMcpLegacySse = GetSystem(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse);
        var systemAiAssistantEnabled = GetSystem(GovernanceSettingKeys.AiAssistant.Enabled);
        var systemAiAssistantProvider = GetSystem(GovernanceSettingKeys.AiAssistant.Provider);
        var systemAiAssistantEndpointUrl = GetSystem(GovernanceSettingKeys.AiAssistant.EndpointUrl);
        var systemAiAssistantApiKey = GetSystem(GovernanceSettingKeys.AiAssistant.ApiKey);
        var systemAiAssistantModelId = GetSystem(GovernanceSettingKeys.AiAssistant.ModelId);
        var systemAiAssistantAllowedModelIds = GetSystem(GovernanceSettingKeys.AiAssistant.AllowedModelIds);
        var systemAiAssistantAllowAnonymousAccess = GetSystem(GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess);
        var systemMcpEnabled = GetSystem(GovernanceSettingKeys.Mcp.Enabled);
        var systemMcpEnableLegacySse = GetSystem(GovernanceSettingKeys.Mcp.EnableLegacySse);
        var systemCommunityGuidelines = GetSystem(GovernanceSettingKeys.Policies.CommunityGuidelinesContent);
        var systemRenderPreset = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Preset);
        var systemRenderAdvanced = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled);
        var systemGlobalRenderMode = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode);
        var systemGlobalPrerender = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled);
        var systemPublicSeoRenderMode = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode);
        var systemPublicSeoPrerender = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled);
        var systemOperationalRenderMode = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode);
        var systemOperationalPrerender = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled);
        var systemAdminRenderMode = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode);
        var systemAdminPrerender = GetSystem(GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled);

        var tenantUserSubmission = GetTenant(GovernanceSettingKeys.Events.UserSubmissionEnabled);
        var tenantOrgSubmission = GetTenant(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled);
        var tenantGroupSubmission = GetTenant(GovernanceSettingKeys.Events.GroupSubmissionEnabled);
        var tenantRequireApproval = GetTenant(GovernanceSettingKeys.Events.RequireApproval);
        var tenantEventCardClickOpensDetailPage = GetTenant(GovernanceSettingKeys.Events.CardClickOpensDetailPage);
        var tenantRequireVerification = GetTenant(GovernanceSettingKeys.Organizations.VerificationRequired);
        var tenantOrgSelfRegistration = GetTenant(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled);
        var tenantGroupSelfRegistration = GetTenant(GovernanceSettingKeys.Groups.SelfRegistrationEnabled);
        var tenantHomePage = GetTenant(GovernanceSettingKeys.Routing.DefaultPublicHomePage);
        var tenantSubdomain = GetTenant(GovernanceSettingKeys.Domains.TenantSubdomain);
        var tenantCustomDomain = GetTenant(GovernanceSettingKeys.Domains.TenantCustomDomain);
        var tenantBrandDisplayName = GetTenant(GovernanceSettingKeys.Branding.DisplayName);
        var tenantBrandLogoUrl = GetTenant(GovernanceSettingKeys.Branding.LogoUrl);
        var tenantBrandFaviconUrl = GetTenant(GovernanceSettingKeys.Branding.FaviconUrl);
        var tenantBrandCustomCssUrl = GetTenant(GovernanceSettingKeys.Branding.CustomCssUrl);
        var tenantAnnouncementEnabled = GetTenant(GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled);
        var tenantAnnouncementMessage = GetTenant(GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage);
        var tenantAnnouncementLinkText = GetTenant(GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText);
        var tenantAnnouncementLinkUrl = GetTenant(GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl);
        var tenantAnnouncementRevision = GetTenant(GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision);
        var tenantAiAssistantEnabled = GetTenant(GovernanceSettingKeys.AiAssistant.Enabled);
        var tenantAiAssistantProvider = GetTenant(GovernanceSettingKeys.AiAssistant.Provider);
        var tenantAiAssistantEndpointUrl = GetTenant(GovernanceSettingKeys.AiAssistant.EndpointUrl);
        var tenantAiAssistantApiKey = GetTenant(GovernanceSettingKeys.AiAssistant.ApiKey);
        var tenantAiAssistantModelId = GetTenant(GovernanceSettingKeys.AiAssistant.ModelId);
        var tenantAiAssistantAllowedModelIds = GetTenant(GovernanceSettingKeys.AiAssistant.AllowedModelIds);
        var tenantAiAssistantAllowAnonymousAccess = GetTenant(GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess);
        var tenantMcpEnabled = GetTenant(GovernanceSettingKeys.Mcp.Enabled);
        var tenantMcpEnableLegacySse = GetTenant(GovernanceSettingKeys.Mcp.EnableLegacySse);
        var tenantCommunityGuidelines = GetTenant(GovernanceSettingKeys.Policies.CommunityGuidelinesContent);

        var isMultiTenant = DeserializeString(systemDeploymentMode?.Value, "SingleTenant").Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);

        var allowTenantRenderOverride = !isMultiTenant || DeserializeBoolean(systemAllowTenantRenderOverride?.Value, false);
        var canOverridePublicSeo = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(systemLockPublicSeo?.Value, false));
        var canOverrideOperational = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(systemLockOperational?.Value, false));
        var canOverrideAdmin = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(systemLockAdmin?.Value, false));

        var tenant = await _tenantRepository.GetById(tenantId);
        var fallbackSubdomain = NormalizeSubdomain(tenant?.Slug) ?? "default";

        var isTenantWhiteLabelingEnabled = isMultiTenant && DeserializeBoolean(systemTenantWhiteLabeling?.Value, false);
        var canOverrideHomePage = systemHomePage?.IsLocked != true;
        var canOverrideEventCardClickBehavior = systemEventCardClickOpensDetailPage?.IsLocked != true;
        var canOverrideSubdomain = systemTenantSubdomain?.IsLocked != true;
        var canOverrideCustomDomain = systemTenantCustomDomain?.IsLocked != true
            && DeserializeBoolean(systemAllowCustomDomain?.Value, true);
        var canOmitVerification = DeserializeBoolean(systemTenantCanOmitVerification?.Value, false)
            && systemRequireVerification?.IsLocked != true;
        var requireVerification = ResolveBoolean(
            tenantRequireVerification?.Value,
            systemRequireVerification?.Value,
            true,
            canOmitVerification);

        var canOverrideAiAssistant = !isMultiTenant || !DeserializeBoolean(systemLockAiAssistant?.Value, true);
        var resolvedAiAssistantModelId = ResolveString(
            tenantAiAssistantModelId?.Value,
            systemAiAssistantModelId?.Value,
            string.Empty,
            canOverrideAiAssistant);
        var resolvedAiAssistantAllowedModelIds = NormalizeAiModelIds(
            [resolvedAiAssistantModelId],
            ResolveStringList(
                tenantAiAssistantAllowedModelIds?.Value,
                systemAiAssistantAllowedModelIds?.Value,
                [],
                canOverrideAiAssistant));

        return new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = ResolveBoolean(
                tenantUserSubmission?.Value,
                systemUserSubmission?.Value,
                true,
                systemUserSubmission?.IsLocked != true),
            AllowOrganizationSubmittedEvents = ResolveBoolean(
                tenantOrgSubmission?.Value,
                systemOrgSubmission?.Value,
                true,
                systemOrgSubmission?.IsLocked != true),
            AllowGroupSubmittedEvents = ResolveBoolean(
                tenantGroupSubmission?.Value,
                systemGroupSubmission?.Value,
                true,
                systemGroupSubmission?.IsLocked != true),
            AllowOrganizationSelfRegistration = ResolveBoolean(
                tenantOrgSelfRegistration?.Value,
                systemOrgSelfRegistration?.Value,
                true,
                systemOrgSelfRegistration?.IsLocked != true),
            AllowGroupSelfRegistration = ResolveBoolean(
                tenantGroupSelfRegistration?.Value,
                systemGroupSelfRegistration?.Value,
                true,
                systemGroupSelfRegistration?.IsLocked != true),
            RequireEventApproval = ResolveBoolean(
                tenantRequireApproval?.Value,
                systemRequireApproval?.Value,
                false,
                systemRequireApproval?.IsLocked != true),
            EventCardClickOpensDetailPage = ResolveBoolean(
                tenantEventCardClickOpensDetailPage?.Value,
                systemEventCardClickOpensDetailPage?.Value,
                false,
                canOverrideEventCardClickBehavior),
            RequireOrganizationVerification = requireVerification,
            CanTenantOmitVerification = canOmitVerification,
            IsTenantWhiteLabelingEnabled = isTenantWhiteLabelingEnabled,
            PreferredHomePage = NormalizeHomePage(ResolveString(
                tenantHomePage?.Value,
                systemHomePage?.Value,
                DefaultPublicHomePage,
                canOverrideHomePage)),
            InstanceBaseDomain = NormalizeOptionalHost(DeserializeString(systemInstanceBaseDomain?.Value, string.Empty)),
            Subdomain = NormalizeSubdomain(ResolveString(
                tenantSubdomain?.Value,
                systemTenantSubdomain?.Value,
                fallbackSubdomain,
                canOverrideSubdomain)) ?? fallbackSubdomain,
            CustomDomain = NormalizeOptionalHost(ResolveString(
                tenantCustomDomain?.Value,
                systemTenantCustomDomain?.Value,
                string.Empty,
                canOverrideCustomDomain)),
            BrandDisplayName = ResolveString(
                tenantBrandDisplayName?.Value,
                systemBrandDisplayName?.Value,
                DefaultBrandDisplayName,
                systemBrandDisplayName?.IsLocked != true),
            BrandLogoUrl = ResolveString(
                tenantBrandLogoUrl?.Value,
                systemBrandLogoUrl?.Value,
                string.Empty,
                isTenantWhiteLabelingEnabled && systemBrandLogoUrl?.IsLocked != true),
            BrandFaviconUrl = ResolveString(
                tenantBrandFaviconUrl?.Value,
                systemBrandFaviconUrl?.Value,
                string.Empty,
                isTenantWhiteLabelingEnabled && systemBrandFaviconUrl?.IsLocked != true),
            BrandCustomCssUrl = ResolveString(
                tenantBrandCustomCssUrl?.Value,
                systemBrandCustomCssUrl?.Value,
                string.Empty,
                isTenantWhiteLabelingEnabled && systemBrandCustomCssUrl?.IsLocked != true),
            AnnouncementBarEnabled = DeserializeBoolean(tenantAnnouncementEnabled?.Value, false),
            AnnouncementBarMessage = DeserializeString(tenantAnnouncementMessage?.Value, string.Empty),
            AnnouncementBarLinkText = DeserializeString(tenantAnnouncementLinkText?.Value, string.Empty),
            AnnouncementBarLinkUrl = DeserializeString(tenantAnnouncementLinkUrl?.Value, string.Empty),
            AnnouncementBarRevision = DeserializeInteger(tenantAnnouncementRevision?.Value, 0),
            CanOverrideHomePagePreference = canOverrideHomePage,
            CanOverrideSubdomain = canOverrideSubdomain,
            CanOverrideCustomDomain = canOverrideCustomDomain,
            CanOverrideBrandDisplayName = systemBrandDisplayName?.IsLocked != true,
            CanOverrideBrandLogoUrl = isTenantWhiteLabelingEnabled && systemBrandLogoUrl?.IsLocked != true,
            CanOverrideBrandFaviconUrl = isTenantWhiteLabelingEnabled && systemBrandFaviconUrl?.IsLocked != true,
            CanOverrideBrandCustomCssUrl = isTenantWhiteLabelingEnabled && systemBrandCustomCssUrl?.IsLocked != true,
            CanOverrideEventCardClickBehavior = canOverrideEventCardClickBehavior,
            RenderPolicyPreset = ResolveString(
                allowTenantRenderOverride ? GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Preset)?.Value : null,
                systemRenderPreset?.Value,
                "AllInteractiveServer",
                allowTenantRenderOverride),
            EnableAdvancedRenderPolicyOverrides = allowTenantRenderOverride
                ? ResolveBoolean(
                    GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled)?.Value,
                    systemRenderAdvanced?.Value,
                    false,
                    true)
                : DeserializeBoolean(systemRenderAdvanced?.Value, false),
            GlobalRenderMode = ResolveString(
                allowTenantRenderOverride ? GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode)?.Value : null,
                systemGlobalRenderMode?.Value,
                "InteractiveServer",
                allowTenantRenderOverride),
            GlobalPrerenderEnabled = allowTenantRenderOverride
                ? ResolveBoolean(
                    GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled)?.Value,
                    systemGlobalPrerender?.Value,
                    false,
                    true)
                : DeserializeBoolean(systemGlobalPrerender?.Value, false),
            PublicSeoRenderMode = ResolveString(
                canOverridePublicSeo ? GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode)?.Value : null,
                systemPublicSeoRenderMode?.Value,
                string.Empty,
                canOverridePublicSeo),
            PublicSeoPrerenderEnabled = canOverridePublicSeo
                ? ResolveBoolean(
                    GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled)?.Value,
                    systemPublicSeoPrerender?.Value,
                    false,
                    true)
                : DeserializeBoolean(systemPublicSeoPrerender?.Value, false),
            OperationalRenderMode = ResolveString(
                canOverrideOperational ? GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode)?.Value : null,
                systemOperationalRenderMode?.Value,
                string.Empty,
                canOverrideOperational),
            OperationalPrerenderEnabled = canOverrideOperational
                ? ResolveBoolean(
                    GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled)?.Value,
                    systemOperationalPrerender?.Value,
                    false,
                    true)
                : DeserializeBoolean(systemOperationalPrerender?.Value, false),
            AdminRenderMode = ResolveString(
                canOverrideAdmin ? GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode)?.Value : null,
                systemAdminRenderMode?.Value,
                string.Empty,
                canOverrideAdmin),
            AdminPrerenderEnabled = canOverrideAdmin
                ? ResolveBoolean(
                    GetTenant(GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled)?.Value,
                    systemAdminPrerender?.Value,
                    false,
                    true)
                : DeserializeBoolean(systemAdminPrerender?.Value, false),
            CanOverrideRenderPolicy = allowTenantRenderOverride,
            CanOverridePublicSeoRenderPolicy = canOverridePublicSeo,
            CanOverrideOperationalRenderPolicy = canOverrideOperational,
            CanOverrideAdminRenderPolicy = canOverrideAdmin,
            CanOverrideSmtp = !isMultiTenant || !DeserializeBoolean(systemLockSmtp?.Value, true),
            CanOverrideStorage = !isMultiTenant || !DeserializeBoolean(systemLockStorage?.Value, true),
            CanOverrideAnalytics = !isMultiTenant || !DeserializeBoolean(systemLockAnalytics?.Value, true),
            CanOverrideAiAssistant = canOverrideAiAssistant,
            CanOverrideMcp = !isMultiTenant || !DeserializeBoolean(systemLockMcp?.Value, true),
            CanOverrideMcpLegacySse = !isMultiTenant || !DeserializeBoolean(systemLockMcpLegacySse?.Value, true),
            AiAssistantEnabled = ResolveBoolean(
                tenantAiAssistantEnabled?.Value,
                systemAiAssistantEnabled?.Value,
                false,
                canOverrideAiAssistant),
            AiAssistantProvider = ResolveString(
                tenantAiAssistantProvider?.Value,
                systemAiAssistantProvider?.Value,
                "none",
                canOverrideAiAssistant),
            AiAssistantEndpointUrl = ResolveString(
                tenantAiAssistantEndpointUrl?.Value,
                systemAiAssistantEndpointUrl?.Value,
                string.Empty,
                canOverrideAiAssistant),
            AiAssistantApiKey = ResolveString(
                tenantAiAssistantApiKey?.Value,
                systemAiAssistantApiKey?.Value,
                string.Empty,
                canOverrideAiAssistant),
            AiAssistantModelId = resolvedAiAssistantModelId,
            AiAssistantAllowedModelIds = resolvedAiAssistantAllowedModelIds,
            AiAssistantAllowAnonymousAccess = ResolveBoolean(
                tenantAiAssistantAllowAnonymousAccess?.Value,
                systemAiAssistantAllowAnonymousAccess?.Value,
                false,
                canOverrideAiAssistant),
            McpEnabled = ResolveBoolean(
                tenantMcpEnabled?.Value,
                systemMcpEnabled?.Value,
                true,
                !isMultiTenant || !DeserializeBoolean(systemLockMcp?.Value, true)),
            McpEnableLegacySse = ResolveBoolean(
                tenantMcpEnableLegacySse?.Value,
                systemMcpEnableLegacySse?.Value,
                false,
                !isMultiTenant || !DeserializeBoolean(systemLockMcpLegacySse?.Value, true)),
            CommunityGuidelinesContent = ResolveString(
                tenantCommunityGuidelines?.Value,
                systemCommunityGuidelines?.Value,
                DefaultCommunityGuidelinesContent,
                systemCommunityGuidelines?.IsLocked != true),
            CanOverrideCommunityGuidelines = systemCommunityGuidelines?.IsLocked != true
        };
    }
}
