// ABOUTME: Shared service registrations used by both Blazor Server (BFF) and WASM host.
// ABOUTME: Eliminates duplication between server Program.cs and client Program.cs (DRY).

using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Providers;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.CustomProperties;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Federation;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;
using Explore.Blazor.Client.Contracts.Services.Footer;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Contracts.Services.LegalDocuments;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Explore.Blazor.Client.Contracts.Services.Reporting;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using Explore.Blazor.Client.Contracts.Services.Waitlist;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Admissions;
using Explore.Blazor.Client.Services.Accessibility;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.ControlPlane;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.EventSessionTemplateSync;
using Explore.Blazor.Client.Services.EventTemplateSync;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Client.Services.Interop;
using Explore.Blazor.Client.Contracts.Services.Scheduling;
using Explore.Blazor.Client.Services.Lookup;
using Explore.Blazor.Client.Services.Scheduling;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Services.Webhooks;
using Explore.Blazor.Client.Services.Waitlist;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ExploreControlPlaneApiAdapter = Explore.Blazor.Client.Services.ControlPlane.ControlPlaneApiAdapter;

namespace Explore.Blazor.Client.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers application services shared between Blazor Server and WebAssembly hosts.
    /// Services that need different implementations per host (e.g., IAnalyticsInterop)
    /// must be registered separately by each host.
    /// </summary>
    public static IServiceCollection AddSharedApplicationServices(
        this IServiceCollection services,
        Action<IServiceProvider, HttpClient>? configureBffRefitClient = null,
        Action<IHttpClientBuilder>? configureBffRefitClientBuilder = null)
    {
        // Domain services backed by specific NSwag per-tag clients
        services.AddScoped<IApiClientExecutor, ApiClientExecutor>();
        services.AddScoped<AdmissionScannerCapabilityState>();
        services.AddScoped<IAdmissionScannerCapabilityState>(provider =>
            provider.GetRequiredService<AdmissionScannerCapabilityState>());
        services.AddTransient<AdmissionScannerCapabilityMessageHandler>();
        services.AddHttpClient(AdmissionScannerHttpClient.ClientName, (provider, client) =>
            configureBffRefitClient?.Invoke(provider, client))
            .AddHttpMessageHandler<AdmissionScannerCapabilityMessageHandler>();
        services.AddScoped<AdmissionScannerHttpClient>();
        services.AddScoped<ExploreControlPlaneApiAdapter>();
        services.AddScoped<IControlPlaneOverviewService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IControlPlaneTenantService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IControlPlaneDomainService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IControlPlaneOperationsService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IControlPlanePlanCatalogService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IControlPlaneTenantConfigurationService>(provider => provider.GetRequiredService<ExploreControlPlaneApiAdapter>());
        services.AddScoped<IConfigurationManifestExportService, ConfigurationManifestExportService>();
        services.AddScoped<LocalIdentityAdministrationService>();
        services.AddScoped<IConfigurationPortabilityService, ConfigurationPortabilityService>();
        services.AddScoped<ISchedulerAdminService, SchedulerAdminApiAdapter>();
        services.AddScoped<IExternalApiKeyService, ExternalApiKeyService>();
        services.AddScoped<IWebhookManagementService, WebhookManagementService>();
        services.AddScoped<IWebhookOperationsService, WebhookOperationsService>();
        services.AddScoped<IListmonkIntegrationSettingsService, ListmonkIntegrationSettingsService>();
        services.AddScoped<IAtprotoFederationSettingsService, AtprotoFederationSettingsService>();
        services.AddScoped<ITenantReportingIntakePolicyService, TenantReportingIntakePolicyService>();
        services.AddScoped<ITenantShellSettingsService, TenantShellSettingsService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IEventSessionService, EventSessionService>();
        services.AddScoped<IEventOrganizerClaimService, EventOrganizerClaimService>();
        services.AddScoped<IEventModerationService, EventModerationService>();
        services.AddScoped<IRegistrationOrderService, RegistrationOrderService>();
        services.AddScoped<IRegistrationPaymentService, RegistrationPaymentService>();
        services.AddScoped<IRegistrationProviderIntegrationService, RegistrationProviderIntegrationService>();
        services.AddScoped<Explore.Blazor.Client.Components.Registration.ProviderLaunch.RegistrationProviderLaunchState>();
        services.AddScoped<INativeRegistrationFormService, NativeRegistrationFormService>();
        services.AddScoped<IGuestRegistrationOrderCapabilityStore, GuestRegistrationOrderCapabilityStore>();
        services.AddScoped<IEventTicketingService, EventTicketingService>();
        services.AddScoped<IEventPromotionService, EventPromotionService>();
        services.AddScoped<IRegistrationFormAuthoringService, RegistrationFormAuthoringService>();
        services.AddScoped<IPlatformMonetizationService, PlatformMonetizationService>();
        services.AddScoped<IPaidEventPolicyService, PaidEventPolicyService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IOrganizationMemberService, OrganizationMemberService>();
        services.AddScoped<ICustomPropertyAdminService, CustomPropertyAdminService>();
        services.AddScoped<ICustomPropertyDefinitionService, CustomPropertyDefinitionService>();
        services.AddScoped<ICustomPropertyValueService, CustomPropertyValueService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.EventTemplates.IEventTemplateService, EventTemplateService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.EventSessionTemplates.IEventSessionTemplateService, EventSessionTemplateService>();
        services.AddScoped<Explore.Blazor.Client.Services.EventTemplateSync.IEventTemplateSyncService, EventTemplateSyncService>();
        services.AddScoped<Explore.Blazor.Client.Services.EventSessionTemplateSync.IEventSessionTemplateSyncService, EventSessionTemplateSyncService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISupportAccessClientService, SupportAccessClientService>();
        services.AddScoped<IAiAssistantClientService, AiAssistantClientService>();
        services.AddScoped<IOrganizationReviewService, OrganizationReviewService>();
        services.AddScoped<ITenantNavigationService, TenantNavigationService>();
        services.AddScoped<IFooterAdminService, FooterAdminService>();
        services.AddScoped<ILegalDocumentService, LegalDocumentService>();
        services.AddBffRefitClient<IMapsApi>(configureBffRefitClient)
            .ConfigureBffRefitClient(configureBffRefitClientBuilder);
        services.AddScoped<IMapsService, MapsService>();
        services.AddScoped<IImageContentClassifier, ImageContentClassifier>();
        services.AddScoped<IImageFileReaderService, ImageFileReaderService>();
        services.AddScoped<IImagePreviewService, ImagePreviewService>();
        services.AddScoped<IStorageObjectUrlResolver, StorageObjectUrlResolver>();
        services.AddScoped<IImageUploadClient, ImageUploadClient>();
        services.AddScoped<IImageStorageService, ImageStorageService>();

        // Lookup / reference data services
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IAddressSuggestionService, AddressSuggestionService>();
        services.AddScoped<IAudienceAgeService, AudienceAgeService>();
        services.AddScoped<IAudienceGenderService, AudienceGenderService>();
        services.AddScoped<IEventFormatService, EventFormatService>();
        services.AddScoped<IEventStatusService, EventStatusService>();
        services.AddScoped<IEventTypeService, EventTypeService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<IMadhabService, MadhabService>();
        services.AddScoped<IEventAspectService, EventAspectService>();
        services.AddScoped<IScheduleItemKindService, ScheduleItemKindService>();
        services.AddScoped<IRegistrationScopeService, RegistrationScopeService>();
        services.AddScoped<IEventRegistrationPolicyService, EventRegistrationPolicyService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService, Explore.Blazor.Client.Services.Lookup.EventLookupService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService, Explore.Blazor.Client.Services.Lookup.DemographicLookupService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService, Explore.Blazor.Client.Services.Lookup.CultureLookupService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.Lookup.IOrganizationLookupService, Explore.Blazor.Client.Services.Lookup.OrganizationLookupService>();
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.Lookup.ISystemLookupService, Explore.Blazor.Client.Services.Lookup.SystemLookupService>();
        services.AddScoped<ILookupCacheService, LookupCacheService>();

        // Event-specific services
        services.AddScoped<Explore.Blazor.Client.Contracts.Services.EventReporting.IEventReportingService, EventReportingService>();
        services.AddScoped<IEventReportModerationService, EventReportModerationService>();
        services.AddScoped<IEventSessionLanguageService, EventSessionLanguageService>();
        services.AddScoped<IEventSessionSpeakerService, EventSessionSpeakerService>();
        services.AddScoped<IEventSessionAgendaItemService, EventSessionAgendaItemService>();
        services.AddScoped<IEventDayService, EventDayService>();
        services.AddScoped<IEventAgendaItemService, EventAgendaItemService>();
        services.AddScoped<ILocationRoomService, LocationRoomService>();
        services.AddScoped<IEventLocationService, EventLocationService>();
        services.AddScoped<IPrivateHomeOwnershipService, PrivateHomeOwnershipService>();
        services.AddScoped<IActorService, ActorService>();
        services.AddScoped<IEventCreationEligibilityService, EventCreationEligibilityService>();
        services.AddScoped<IEventTeamService, EventTeamService>();
        services.AddScoped<IContactShareConsentService, ContactShareConsentService>();
        services.AddScoped<
            ITicketPurchaseGovernanceService,
            TicketPurchaseGovernanceService>();
        services.AddScoped<
            IParticipantReadinessService,
            ParticipantReadinessService>();
        services.AddScoped<
            ITicketTransferService,
            TicketTransferService>();
        services.AddScoped<
            IFairReturnWaitlistService,
            FairReturnWaitlistService>();
        services.AddScoped<IEventAddOnService, EventAddOnService>();
        services.AddScoped<
            ITicketingDeploymentCapabilityService,
            TicketingDeploymentCapabilityService>();

        // Notification services
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IActorSubscriptionService, ActorSubscriptionService>();
        services.AddScoped<INotificationRefreshStreamClient, NotificationRefreshStreamClient>();
        services.AddScoped<IWebPushBrowserInterop, WebPushBrowserInterop>();
        services.AddScoped<IHomeDiscoveryGeolocation, HomeDiscoveryGeolocation>();
        services.AddScoped<IAdmissionQrScanner, AdmissionQrScannerInterop>();

        // BFF / onboarding services (use named HttpClient "BffClient")
        services.AddBffRefitClient<IBffAuthApi>(configureBffRefitClient)
            .ConfigureBffRefitClient(configureBffRefitClientBuilder);
        services.AddScoped<IInstanceOnboardingService, InstanceOnboardingService>();
        services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();
        services.AddScoped<IPublicExperienceService, PublicExperienceService>();
        services.AddScoped<IHomeDiscoveryService, HomeDiscoveryService>();
        services.AddScoped<ITenantPublicExperienceAdminService, TenantPublicExperienceAdminService>();
        services.AddScoped<ITenantBrandingSettingsAdminService, TenantBrandingSettingsAdminService>();
        services.AddScoped<
            ITenantDirectoryOperatorIdentityAdminService,
            TenantDirectoryOperatorIdentityAdminService>();
        services.AddScoped<ITenantStorageSettingsAdminService, TenantStorageSettingsAdminService>();
        services.AddScoped<IAppearanceThemeService, AppearanceThemeService>();
        services.AddScoped<IUserAppearancePreferencesService, UserAppearancePreferencesService>();

        // Runtime render policy and routing
        services.AddScoped<IRuntimeRenderPolicyService, RuntimeRenderPolicyService>();
        services.AddScoped<IStartupRoutingService, StartupRoutingService>();

        // Localization
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddBffRefitClient<ILanguagePreferenceApi>(configureBffRefitClient)
            .ConfigureBffRefitClient(configureBffRefitClientBuilder);
        services.AddScoped<ILanguagePreferenceService, LanguagePreferenceService>();
        services.AddTransient<MudBlazor.MudLocalizer, MudBlazorLocalizer>();

        // UI state
        services.AddScoped<CurrentUserState>();
        services.AddScoped<MainContentAppearanceState>();
        services.AddScoped<AiAssistantState>();
        services.AddScoped<AiAssistantConversationState>();
        services.AddScoped<TenantNavLinksState>();
        services.AddScoped<DockLayoutState>();
        services.AddScoped<IDockPanelRegistry>(provider => provider.GetRequiredService<DockLayoutState>());
        services.AddScoped<LocalStorageDockLayoutPersistence>();
        services.AddScoped<IDockLayoutPersistence>(provider => new ServerBackedDockLayoutPersistence(
            provider.GetRequiredService<LocalStorageDockLayoutPersistence>(),
            provider.GetRequiredService<IUserSettingsService>(),
            provider.GetRequiredService<AuthenticationStateProvider>(),
            provider.GetRequiredService<IUiShellContextService>(),
            provider.GetRequiredService<ILogger<ServerBackedDockLayoutPersistence>>()));
        services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        services.AddScoped<WorkspaceRouteClassifier>();
        services.AddScoped<UiShellState>();
        services.AddScoped<IShellPreferencesService, ShellPreferencesService>();
        services.AddScoped<StudioEventContextState>();
        services.AddScoped<IStudioContextService, StudioContextService>();
        services.AddScoped<IUiShellContextService, UiShellContextService>();

        // User-scoped settings (auth-branching: API for authenticated, localStorage for anonymous)
        services.AddScoped<IUserSettingsService, UserSettingsService>();

        // Accessibility services (ARIA announcements + focus management)
        services.AddScoped<IAccessibilityAnnouncerService, AccessibilityAnnouncerService>();
        services.AddScoped<IAccessibilityFocusService, AccessibilityFocusService>();
        services.AddScoped<IAdmissionRecoveryBffClient, AdmissionRecoveryBffClient>();
        services.AddScoped<IAdmissionTicketService, AdmissionTicketService>();
        services.AddScoped<IAdmissionCheckInService, AdmissionCheckInService>();
        services.AddScoped<IAdmissionRecoveryFragmentInterop, AdmissionRecoveryFragmentInterop>();
        services.AddScoped<IAdmissionTicketPrintInterop, AdmissionTicketPrintInterop>();
        services.AddScoped<IBrowserActionInterop, BrowserActionInterop>();

        // Feature flags (hydrated from API, no OpenFeature SDK dependency)
        services.AddScoped<FeatureStateContainer>();
        services.AddScoped<IFeatureFlagClientService, FeatureFlagClientService>();

        return services;
    }


    private static IHttpClientBuilder ConfigureBffRefitClient(
        this IHttpClientBuilder builder,
        Action<IHttpClientBuilder>? configure)
    {
        configure?.Invoke(builder);
        return builder;
    }
}
