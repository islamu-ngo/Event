using System.Text.Json;
using AutoMapper;
using Explore.Application.Analytics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Enums.Analytics;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Handlers.Queries;

public class GetPublicExperienceSettingsQueryHandler : IRequestHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto>
{
    private readonly ITenantContext _tenantContext;
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IAnalyticsConfigResolver _analyticsConfigResolver;
    private readonly ITranslationConfigResolver _translationConfigResolver;
    private readonly ITenantPolicySettingService _policySettingService;
    private readonly IModuleService _moduleService;
    private readonly IInstanceGovernanceSettingService _instanceGovernanceSettingService;
    private readonly IAnalyticsGovernanceService _analyticsGovernanceService;
    private readonly IAnalyticsRuntimeProfileResolver _runtimeProfileResolver;
    private readonly IHierarchicalSettingsResolver _hierarchicalSettingsResolver;
    private readonly ITypedSettingsDocumentResolver _typedSettingsDocumentResolver;
    private readonly IFooterLinkGroupRepository _footerLinkGroupRepository;
    private readonly IMapper _mapper;
    private readonly ITenantDirectoryOperatorReadinessEvaluator _directoryOperatorReadiness;
    private readonly IInstanceOperatorIdentity _instanceOperatorIdentity;
    private readonly IVisitorAccessCapabilityResolver _visitorAccessCapabilityResolver;

    public GetPublicExperienceSettingsQueryHandler(
        ITenantContext tenantContext,
        ISystemSettingRepository systemSettingRepository,
        IAnalyticsConfigResolver analyticsConfigResolver,
        ITranslationConfigResolver translationConfigResolver,
        ITenantPolicySettingService policySettingService,
        IModuleService moduleService,
        IInstanceGovernanceSettingService instanceGovernanceSettingService,
        IAnalyticsGovernanceService analyticsGovernanceService,
        IAnalyticsRuntimeProfileResolver runtimeProfileResolver,
        IHierarchicalSettingsResolver hierarchicalSettingsResolver,
        ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
        IFooterLinkGroupRepository footerLinkGroupRepository,
        IMapper mapper,
        ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness,
        IInstanceOperatorIdentity instanceOperatorIdentity,
        IVisitorAccessCapabilityResolver visitorAccessCapabilityResolver)
    {
        _tenantContext = tenantContext;
        _systemSettingRepository = systemSettingRepository;
        _analyticsConfigResolver = analyticsConfigResolver;
        _translationConfigResolver = translationConfigResolver;
        _policySettingService = policySettingService;
        _moduleService = moduleService;
        _instanceGovernanceSettingService = instanceGovernanceSettingService;
        _analyticsGovernanceService = analyticsGovernanceService;
        _runtimeProfileResolver = runtimeProfileResolver;
        _hierarchicalSettingsResolver = hierarchicalSettingsResolver;
        _typedSettingsDocumentResolver = typedSettingsDocumentResolver;
        _footerLinkGroupRepository = footerLinkGroupRepository;
        _mapper = mapper;
        _directoryOperatorReadiness = directoryOperatorReadiness;
        _instanceOperatorIdentity = instanceOperatorIdentity;
        _visitorAccessCapabilityResolver = visitorAccessCapabilityResolver;
    }

    public async Task<PublicExperienceSettingsDto> Handle(GetPublicExperienceSettingsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        TenantDirectoryOperatorReadinessAssessment directoryAssessment =
            await _directoryOperatorReadiness.EvaluateAsync(
                tenantId,
                TenantDirectoryOperatorIdentityCapability.PublicDisclosure,
                cancellationToken);
        if (!directoryAssessment.IsReady
            || directoryAssessment.Identity is null
            || directoryAssessment.DocumentRevision is not { } documentRevision
            || documentRevision == Guid.Empty)
        {
            return new PublicExperienceSettingsDto
            {
                TenantId = tenantId,
                IsAvailable = false,
                UnavailableCode = "tenant_identity_unavailable"
            };
        }

        var effectiveTenantSettings = await _policySettingService.ReadEffectiveTenantSettingsAsync(tenantId);
        var brandingDocument = await ResolveTypedBrandingAsync(tenantId, cancellationToken);
        var enabledModulesInfo = await _moduleService.GetEnabledModulesAsync(tenantId, cancellationToken);
        var enabledModuleKeys = enabledModulesInfo.Select(m => m.ModuleKey).ToList();
        var governanceSettings = await _instanceGovernanceSettingService.ReadEffectiveSettingsForTenantAsync(tenantId);
        var analyticsConfiguration = await _analyticsConfigResolver.ResolveAsync(cancellationToken);
        var translationConfiguration = await _translationConfigResolver.ResolveAsync(cancellationToken);

        var deploymentModeSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Deployment.Mode, cancellationToken);
        var deploymentMode = DeserializeString(deploymentModeSetting?.Value, "SingleTenant");
        var analyticsProvider = analyticsConfiguration.Provider.ToString().ToLowerInvariant();
        var analyticsPublicApiKey = analyticsConfiguration.ApiKey;
        var analyticsEndpointUrl = analyticsConfiguration.EndpointUrl;
        var hasAnalyticsApiKey = !string.IsNullOrWhiteSpace(analyticsPublicApiKey);
        var transportRequiresPublicApiKey = analyticsConfiguration.TransportMode != AnalyticsTransportMode.Relay;
        var shouldEnableAnalytics = analyticsConfiguration.IsEnabled
            && analyticsConfiguration.Provider != Domain.Enums.AnalyticsProviderEnum.None
            && (!transportRequiresPublicApiKey || hasAnalyticsApiKey);
        var analyticsConsentMode = analyticsConfiguration.ConsentMode.ToString().ToLowerInvariant();
        var analyticsTransportMode = analyticsConfiguration.TransportMode.ToString().ToLowerInvariant();
        var analyticsAllowIdentify = _analyticsGovernanceService.AllowsIdentify(analyticsConfiguration.Provider, analyticsConfiguration.ConsentMode);

        // Resolve consent bootstrap via the runtime profile resolver
        var analyticsSettingGroup = await _hierarchicalSettingsResolver.ResolveGroupAsync<AnalyticsSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        analyticsSettingGroup.TenantStableKey = tenantId.ToString("N")[..8];
        var runtimeProfile = _runtimeProfileResolver.Resolve(analyticsSettingGroup);
        var consentBootstrap = MapToConsentBootstrap(runtimeProfile, analyticsProvider);

        // Resolve footer config (settings + link groups)
        var footerSettingGroup = await _hierarchicalSettingsResolver.ResolveGroupAsync<FooterSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        var footerLinkGroups = await _footerLinkGroupRepository.GetResolvedGroupsForTenantAsync(tenantId, cancellationToken);

        var footerConfig = new FooterConfigDto
        {
            Settings = new FooterSettingsDto
            {
                Enabled = footerSettingGroup.Enabled,
                Template = footerSettingGroup.Template,
                ShowDescription = footerSettingGroup.ShowDescription,
                DescriptionText = footerSettingGroup.DescriptionText,
                ShowSocialLinks = footerSettingGroup.ShowSocialLinks,
                SocialLinks = footerSettingGroup.SocialLinks.AsReadOnly(),
                CopyrightText = footerSettingGroup.CopyrightText,
                ShowCookieSettingsLink = footerSettingGroup.ShowCookieSettingsLink,
            },
            LinkGroups = _mapper.Map<List<FooterLinkGroupDto>>(footerLinkGroups),
        };

        // Resolve AI assistant availability (enabled + configured API key)
        var aiSettingGroup = await _hierarchicalSettingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);

        var appearanceSettingGroup = await _hierarchicalSettingsResolver.ResolveGroupAsync<AppearanceSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);

        return new PublicExperienceSettingsDto
        {
            TenantId = tenantId,
            IsAvailable = true,
            VisitorAccess = VisitorAccessCapabilityDto.From(
                await _visitorAccessCapabilityResolver.ResolveAsync(tenantId, cancellationToken)),
            DirectoryOperator = MapDirectoryOperator(directoryAssessment.Identity, documentRevision),
            InstanceOperator = MapInstanceOperator(_instanceOperatorIdentity),
            Mode = Explore.Application.Models.PublicExperienceMode.DiscoveryCentric,
            DeploymentMode = deploymentMode,
            PreferredHomePage = effectiveTenantSettings.PreferredHomePage,
            BrandDisplayName = brandingDocument?.Payload.DisplayName ?? string.Empty,
            BrandLogoUrl = brandingDocument?.Payload.LogoUrl ?? string.Empty,
            BrandFaviconUrl = brandingDocument?.Payload.FaviconUrl ?? string.Empty,
            BrandCustomCssUrl = brandingDocument?.Payload.CustomCssUrl ?? string.Empty,
            InstanceBaseDomain = effectiveTenantSettings.InstanceBaseDomain,
            Subdomain = effectiveTenantSettings.Subdomain,
            CustomDomain = effectiveTenantSettings.CustomDomain,
            AllowUserSubmittedEvents = effectiveTenantSettings.AllowUserSubmittedEvents,
            AllowOrganizationSubmittedEvents = effectiveTenantSettings.AllowOrganizationSubmittedEvents,
            AllowGroupSubmittedEvents = effectiveTenantSettings.AllowGroupSubmittedEvents,
            AllowOrganizationSelfRegistration = effectiveTenantSettings.AllowOrganizationSelfRegistration,
            AllowGroupSelfRegistration = effectiveTenantSettings.AllowGroupSelfRegistration,
            EventCardClickOpensDetailPage = effectiveTenantSettings.EventCardClickOpensDetailPage,
            AnnouncementBarEnabled = effectiveTenantSettings.AnnouncementBarEnabled,
            AnnouncementBarMessage = effectiveTenantSettings.AnnouncementBarMessage,
            AnnouncementBarLinkText = effectiveTenantSettings.AnnouncementBarLinkText,
            AnnouncementBarLinkUrl = effectiveTenantSettings.AnnouncementBarLinkUrl,
            AnnouncementBarRevision = effectiveTenantSettings.AnnouncementBarRevision,
            CommunityGuidelinesContent = effectiveTenantSettings.CommunityGuidelinesContent,
            IsIslamicModuleEnabled = enabledModuleKeys.Contains("Mod_Islamic"),
            IsTechModuleEnabled = enabledModuleKeys.Contains("Mod_Tech"),
            EnabledModules = enabledModuleKeys,
            AnalyticsProvider = analyticsProvider,
            AnalyticsEnabled = shouldEnableAnalytics,
            AnalyticsConsentMode = analyticsConsentMode,
            AnalyticsTransportMode = analyticsTransportMode,
            AnalyticsAllowIdentify = analyticsAllowIdentify,
            AnalyticsPublicApiKey = analyticsPublicApiKey ?? string.Empty,
            AnalyticsEndpointUrl = analyticsEndpointUrl ?? string.Empty,
            AnalyticsConsent = consentBootstrap,
            RenderPolicyVersion = governanceSettings.RenderPolicy.RenderPolicyVersion,
            RenderPolicyPreset = governanceSettings.RenderPolicy.RenderPolicyPreset,
            EnableAdvancedRenderPolicyOverrides = governanceSettings.RenderPolicy.EnableAdvancedRenderPolicyOverrides,
            GlobalRenderMode = governanceSettings.RenderPolicy.GlobalRenderMode,
            GlobalPrerenderEnabled = governanceSettings.RenderPolicy.GlobalPrerenderEnabled,
            PublicSeoRenderMode = governanceSettings.RenderPolicy.PublicSeoRenderMode,
            PublicSeoPrerenderEnabled = governanceSettings.RenderPolicy.PublicSeoPrerenderEnabled,
            OperationalRenderMode = governanceSettings.RenderPolicy.OperationalRenderMode,
            OperationalPrerenderEnabled = governanceSettings.RenderPolicy.OperationalPrerenderEnabled,
            AdminRenderMode = governanceSettings.RenderPolicy.AdminRenderMode,
            AdminPrerenderEnabled = governanceSettings.RenderPolicy.AdminPrerenderEnabled,
            OnboardingRenderMode = governanceSettings.RenderPolicy.OnboardingRenderMode,
            OnboardingPrerenderEnabled = governanceSettings.RenderPolicy.OnboardingPrerenderEnabled,
            DisallowInteractiveServerOnOnboarding = governanceSettings.RenderPolicy.DisallowInteractiveServerOnOnboarding,
            IsAiAssistantEnabled = aiSettingGroup.Enabled,
            IsAiAssistantAvailable = aiSettingGroup.IsAvailable,
            AiAssistantAllowAnonymousAccess = aiSettingGroup.AllowAnonymousAccess,
            FooterConfig = footerConfig,
            DefaultThemeId = appearanceSettingGroup.ActiveProfileId,
            ThemeMode = appearanceSettingGroup.ThemeMode,
            Direction = appearanceSettingGroup.Direction,
            Language = appearanceSettingGroup.Language,
            ClientPickerEnabled = translationConfiguration.ClientPickerEnabled,
        };
    }

    private static TenantDirectoryOperatorPublicDto MapDirectoryOperator(
        TenantDirectoryOperatorIdentity identity,
        Guid documentRevision) =>
        new()
        {
            DocumentRevision = documentRevision,
            PublicName = identity.PublicName,
            LegalName = identity.LegalName,
            OperatorKindCode = identity.OperatorKindCode,
            JurisdictionCountryCode = identity.JurisdictionCountryCode,
            RegistrationIdentifier = identity.RegistrationIdentifier,
            PublicContactEmail = identity.PublicContactEmail,
            LegalNoticeUrl = identity.LegalNoticeUrl,
            TermsUrl = identity.TermsUrl,
            PrivacyUrl = identity.PrivacyUrl
        };

    private static InstanceOperatorPublicDto MapInstanceOperator(IInstanceOperatorIdentity identity) =>
        new()
        {
            OperatorId = identity.OperatorId,
            PublicName = identity.PublicName,
            LegalName = identity.LegalName,
            IsOfficialInstance = identity.IsOfficialInstance,
            OfficialOrigin = identity.OfficialOrigin,
            OperatorKindCode = identity.OperatorKindCode,
            JurisdictionCountryCode = identity.JurisdictionCountryCode,
            RegistrationIdentifier = identity.RegistrationIdentifier,
            PublicContactEmail = identity.PublicContactEmail,
            WebsiteUrl = identity.WebsiteUrl,
            LegalNoticeUrl = identity.LegalNoticeUrl,
            TermsUrl = identity.TermsUrl,
            PrivacyUrl = identity.PrivacyUrl
        };

    private Task<ResolvedSettingsDocument<BrandingSettings>?> ResolveTypedBrandingAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string documentKey = SettingsDocumentKeys.Tenant.Branding;
        var context = new SettingsResolutionContext(tenantId, RequestedDocuments: [documentKey]);

        return _typedSettingsDocumentResolver.ResolveTenantDocumentAsync<BrandingSettings>(
            context,
            documentKey,
            cancellationToken);
    }

    private static AnalyticsConsentBootstrapDto MapToConsentBootstrap(AnalyticsRuntimeProfile profile, string provider)
    {
        var bootstrap = new AnalyticsConsentBootstrapDto
        {
            CookieBannerEnabled = profile.CookieBannerEnabled,
            CanRunBeforeConsent = profile.CanRunBeforeConsent,
            DeclineBehavior = profile.DeclineBehavior switch
            {
                DeclineBehavior.Cookieless => "cookieless",
                DeclineBehavior.Disable => "disable",
                _ => "disable"
            },
            ConsentCookieKey = profile.ConsentCookieKey,
            ConsentCookieLifetimeDays = profile.ConsentCookieLifetimeDays,
            AnalyticsProvider = provider
        };

        if (profile.Posthog is not null)
        {
            bootstrap.Posthog = new PosthogClientBootstrapDto
            {
                CookielessMode = profile.Posthog.CookielessMode switch
                {
                    PosthogCookielessMode.Always => "always",
                    PosthogCookielessMode.OnReject => "on_reject",
                    _ => "off"
                },
                PersonProfiles = profile.Posthog.PersonProfiles switch
                {
                    PosthogPersonProfiles.Always => "always",
                    PosthogPersonProfiles.Never => "never",
                    _ => "identified_only"
                },
                SessionReplay = profile.Posthog.SessionReplay,
                Autocapture = profile.Posthog.Autocapture,
                Heatmaps = profile.Posthog.Heatmaps,
                Toolbar = profile.Posthog.Toolbar
            };
        }

        return bootstrap;
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }
}
