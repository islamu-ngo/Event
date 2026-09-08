using System.Reflection;
using AutoMapper.Internal;
using Explore.Application.Analytics;
using Explore.Application.Authorization;
using Explore.Application.Behaviors;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.Authentication.Atproto.Services;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.CustomPropertyDefinitions.Authorization;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.EventCategories.Authorization;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Authorization;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventOrganizerClaims.Authorization;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Features.EventReporting;
using Explore.Application.Features.Events;
using Explore.Application.Features.EventTicketing.Services;
using Explore.Application.Features.EventSessionAgendaItems.Authorization;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Authorization;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionGroups.Authorization;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Authorization;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Authorization;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Authorization;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Features.EventTags.Authorization;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Features.EventTemplates.Authorization;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Features.Footer.Handlers.Commands;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Features.RegistrationOrders.Handlers.Commands;
using Explore.Application.Features.StorageObjects.Authorization;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Services.Federation;
using Explore.Application.Services.Lifecycle;
using Explore.Application.Services.Registration;
using Explore.Application.Services.Waitlist;
using Explore.Application.Services.Webhooks;
using Explore.Application.Settings;
using Explore.Application.Telemetry;
using Explore.Application.Webhooks;
using Explore.Domain.Services.Scheduling;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Application;

public static class ApplicationServicesRegistration
{
    public static IServiceCollection ConfigureApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateInstanceOperatorIdentityOnStart = true)
    {
        PrivacyErasureDurabilityOptions erasureDurability =
            PrivacyErasureDurabilityOptions.FromConfiguration(configuration);
        services.AddOptions<PrivacyErasureDurabilityOptions>()
            .Configure(options =>
            {
                options.Topology = erasureDurability.Topology;
            });
        services.AddOptions<PrivacyErasureOptions>()
            .Bind(configuration.GetSection(PrivacyErasureOptions.SectionName))
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Privacy-erasure lifecycle settings are invalid.")
            .ValidateOnStart();

        services.AddAutoMapper(cfg =>
        {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
            // AutoMapper 15+ requires a Lucky Penny commercial license key at runtime.
            // Injected from Infisical /api folder: LUCKYPENNY_LICENSE_KEY → Licensing:LuckyPenny:LicenseKey.
            // No throw here: the OpenAPI doc generator runs Program.Main at build time without secrets.
            // Lucky Penny libraries themselves enforce licensing at runtime.
            var licenseKey = configuration["Licensing:LuckyPenny:LicenseKey"];
            if (!string.IsNullOrEmpty(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }
#endif
            // Bound every map traversal in the FOSS line to mitigate CVE-2026-32933.
            // The same ceiling is defense in depth for commercial vendor-patched builds.
            cfg.Internal().ForAllMaps((_, mapping) => mapping.MaxDepth(64));
            cfg.AddMaps(Assembly.GetExecutingAssembly());
        });

        services.AddMediatR(cfg =>
        {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
            // MediatR 13+ requires a Lucky Penny commercial license key at runtime.
            // Same key as AutoMapper — single LUCKYPENNY_LICENSE_KEY from Infisical.
            var licenseKey = configuration["Licensing:LuckyPenny:LicenseKey"];
            if (!string.IsNullOrEmpty(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }
#endif
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServicesRegistration).Assembly);
        });

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddTransient<AuthorizationResourceContextResolver>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateCustomPropertyDefinitionCommand>, UpdateCustomPropertyDefinitionAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventCustomPropertyDefinitionCommand>, UpdateEventCustomPropertyDefinitionAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionCustomPropertyDefinitionCommand>, UpdateEventSessionCustomPropertyDefinitionAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventTemplateCommand>, UpdateEventTemplateAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionTemplateCommand>, UpdateEventSessionTemplateAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionLanguageCommand>, UpdateEventSessionLanguageAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventCategoriesCommand>, UpdateEventCategoriesAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventTagsCommand>, UpdateEventTagsAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionAgendaItemCommand>, UpdateEventSessionAgendaItemAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionGroupCommand>, UpdateEventSessionGroupAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<UpdateEventSessionSpeakerCommand>, UpdateEventSessionSpeakerAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<WithdrawEventOrganizerClaimCommand>, WithdrawEventOrganizerClaimAuthorizationContextEnricher>();
        services.AddTransient<IAuthorizationContextEnricher<CreateStorageUploadSessionCommand>, CreateStorageUploadSessionAuthorizationContextEnricher>();
        services.AddOptions<EventReportSubmissionOptions>()
            .Bind(configuration.GetSection(EventReportSubmissionOptions.SectionName))
            .Validate(
                options => EventReportSubmissionOptions.IsValidCaseSlaHours(options.CaseSlaHours),
                $"Reporting:CaseSlaHours must be between {EventReportSubmissionOptions.MinCaseSlaHours} and {EventReportSubmissionOptions.MaxCaseSlaHours} hours.")
            .ValidateOnStart();
        services.AddOptions<EventReminderOptions>()
            .Bind(configuration.GetSection(EventReminderOptions.SectionName))
            .Validate(
                options => EventReminderOptions.IsValidLeadTimeHours(options.EventReminderLeadTimeHours),
                $"EmailDispatch:EventReminderLeadTimeHours must be between {EventReminderOptions.MinLeadTimeHours} and {EventReminderOptions.MaxLeadTimeHours} hours.")
            .ValidateOnStart();
        services.AddOptions<EventLocationPrivacyObservabilityOptions>()
            .Bind(configuration.GetSection(EventLocationPrivacyObservabilityOptions.SectionName))
            .Validate(
                options => EventLocationPrivacyObservabilityOptions.IsValidReviewQueueDegradedThreshold(
                    options.ReviewQueueDegradedThreshold),
                $"LocationPrivacy:Observability:ReviewQueueDegradedThreshold must be between {EventLocationPrivacyObservabilityOptions.MinReviewQueueDegradedThreshold} and {EventLocationPrivacyObservabilityOptions.MaxReviewQueueDegradedThreshold}.")
            .ValidateOnStart();
        services.AddOptions<OrganizerPaymentReadinessReconciliationOptions>()
            .Bind(configuration.GetSection(OrganizerPaymentReadinessReconciliationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<OrganizerPaymentCommerceOptions>()
            .Bind(configuration.GetSection(OrganizerPaymentCommerceOptions.SectionName));
        services.AddOptions<PaidCheckoutGovernanceOptions>()
            .Bind(configuration.GetSection(PaidCheckoutGovernanceOptions.SectionName));
        services.AddSingleton<IValidateOptions<InstanceOperatorIdentityOptions>, InstanceOperatorIdentityOptionsValidator>();
        OptionsBuilder<InstanceOperatorIdentityOptions> instanceIdentityOptions =
            services.AddOptions<InstanceOperatorIdentityOptions>()
                .Bind(configuration.GetSection(InstanceOperatorIdentityOptions.SectionName));
        if (validateInstanceOperatorIdentityOnStart)
        {
            instanceIdentityOptions.ValidateOnStart();
        }
        services.AddSingleton<TenantDirectoryOperatorReadinessTelemetry>();
        services.AddScoped<
            ITenantDirectoryOperatorReadinessEvaluator,
            TenantDirectoryOperatorReadinessEvaluator>();
        services.AddOptions<PromotionCodeLookupOptions>()
            .Bind(configuration.GetSection(PromotionCodeLookupOptions.SectionName))
            .Validate(
                options => options.ActiveKeyVersion >= 1,
                "Promotions:CodeLookup:ActiveKeyVersion must be greater than or equal to 1.")
            .ValidateOnStart();
        services.AddOptions<AdmissionCredentialOptions>()
            .Bind(configuration.GetSection(AdmissionCredentialOptions.SectionName))
            .Validate(options =>
            {
                try
                {
                    _ = options.GetDigestKeyVersions();
                    return options.RetainedKeyVersions.Append(options.ActiveKeyVersion).Distinct().Count() ==
                        options.RetainedKeyVersions.Length + 1;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Admissions:CredentialLookup key versions must be positive, unique, and bounded.")
            .ValidateOnStart();
        services.AddOptions<AdmissionRecoveryOptions>()
            .Bind(configuration.GetSection(AdmissionRecoveryOptions.SectionName))
            .Validate(
                options => options.ActiveKeyVersion >= 1 &&
                    options.RetainedKeyVersions.All(version => version >= 1) &&
                    options.RetainedKeyVersions.Distinct().Count() == options.RetainedKeyVersions.Length &&
                    options.CapabilityLifetimeMinutes is >= 5 and <= 1440 &&
                    options.RateLimitBucketCount is >= 64 and <= 65_536 &&
                    options.RateLimitPermitCount is >= 1 and <= 100 &&
                    options.RateLimitWindowSeconds is >= 60 and <= 86_400,
                "Admissions:Recovery configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<IAdmissionTargetMaterializer, AdmissionTargetMaterializer>();
        services.AddScoped<AdmissionIssuanceService>();
        services.AddScoped<IAdmissionIssuanceService>(provider =>
            provider.GetRequiredService<AdmissionIssuanceService>());
        services.AddScoped<AnonymousCancellationService>();
        services.AddScoped<AdmissionRevocationService>();
        services.AddScoped<IAdmissionRevocationService>(provider =>
            provider.GetRequiredService<AdmissionRevocationService>());
        services.AddScoped<IAdmissionRefundRevocationService, AdmissionRefundRevocationService>();
        services.AddScoped<IAdmissionEventCancellationService, AdmissionEventCancellationService>();
        services.AddScoped<AdmissionRecoveryService>();
        services.AddScoped<AdmissionRecoveryRedemptionService>();
        services.AddScoped<AdmissionCheckInService>();
        services.AddScoped<AdmissionCheckInReportingService>();
        services.AddScoped<AdmissionCheckInOperationsService>();
        services.AddSingleton<IAdmissionCheckInTelemetry, AdmissionCheckInMetrics>();
        services.AddScoped<AdmissionScannerAuthenticationService>();
        services.AddScoped<IAdmissionScannerAuthenticationService>(provider =>
            provider.GetRequiredService<AdmissionScannerAuthenticationService>());
        services.AddScoped<AdmissionScannerCapabilityService>();
        services.AddOptions<AdmissionScannerCapabilityDigestOptions>()
            .Bind(configuration.GetSection(AdmissionScannerCapabilityDigestOptions.SectionName))
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Admissions:ScannerCapabilityDigest configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<AdmissionTicketAccountDeliveryService>();
        services.AddScoped<IAdmissionRecoveryTicketDocumentService, AdmissionRecoveryTicketDocumentService>();
        services.AddScoped<IAdmissionRecoveryAuditService, AdmissionRecoveryAuditService>();
        services.AddScoped<IOrganizerPaymentCommerceConfiguration>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrganizerPaymentCommerceOptions>>().Value);
        services.AddScoped<IPaidCheckoutGovernance>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaidCheckoutGovernanceOptions>>().Value);
        services.AddSingleton<IInstanceOperatorIdentity>(provider =>
            InstanceOperatorIdentity.Create(
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<InstanceOperatorIdentityOptions>>().Value));
        services.AddSingleton<IValidateOptions<OrganizerPaymentReadinessReconciliationOptions>, OrganizerPaymentReadinessReconciliationOptionsValidator>();
        services.AddOptions<RegistrationFileAnswerOptions>()
            .Bind(configuration.GetSection(RegistrationFileAnswerOptions.SectionName));
        services.Configure<NotificationRoutingOptions>(configuration.GetSection(NotificationRoutingOptions.SectionName));
        services.Configure<AccountAuthorityLifecycleEmailOptions>(configuration.GetSection(AccountAuthorityLifecycleEmailOptions.SectionName));
        services.AddSingleton<IAiToolContractRegistry>(_ => AiToolContractRegistry.CreateDefault());
        services.AddScoped<IAiAssistantActorContextService, AiAssistantActorContextService>();
        services.AddScoped<AtprotoSubjectOnboardingOperation>();
        services.AddScoped<AtprotoJitAccountProvisioningOperation>();
        services.AddScoped<IAiContextGateway, AiContextGateway>();
        services.AddScoped<IAiProviderTrustResolver, DefaultAiProviderTrustResolver>();
        services.AddScoped<IAiContextRedactor, AiContextRedactor>();
        services.AddScoped<IAiContextHygieneService, AiContextHygieneService>();

        // Onboarding Services
        services.AddScoped<ITenantPolicySettingService, TenantPolicySettingService>();
        services.AddScoped<ITenantStorageSettingService, TenantStorageSettingService>();
        services.AddConfigurationManifestApplication();
        services.AddScoped<ITenantBrandingSettingsDocumentProvisioningService, TenantBrandingSettingsDocumentProvisioningService>();
        services.AddScoped<ITenantBrandingSettingsDocumentLockService, TenantBrandingSettingsDocumentLockService>();
        services.AddScoped<FooterLinkMutationGuard>();
        services.AddScoped<IInstanceGovernanceSettingService, InstanceGovernanceSettingService>();
        services.AddScoped<IInstanceStorageSettingService, InstanceStorageSettingService>();
        services.AddScoped<IInstanceSmtpSettingService, InstanceSmtpSettingService>();
        services.AddScoped<IInstanceBootstrapAuditLogger, InstanceBootstrapAuditLogger>();
        services.AddScoped<InstanceOnboardingCompletionOperation>();
        services.AddScoped<IWebhookAuditEventWriter, WebhookAuditEventWriter>();
        services.AddScoped<IWebhookOwnershipScopeResolver, WebhookOwnershipScopeResolver>();
        services.AddScoped<IAuthProviderConfigurationService, AuthProviderConfigurationService>();
        services.AddScoped<IVisitorAccessProviderReader, VisitorAccessProviderReader>();
        services.AddScoped<IVisitorAccessCapabilityResolver, VisitorAccessCapabilityResolver>();
        services.AddScoped<IKeycloakIdentityContractContributor, EventKeycloakIdentityContractContributor>();
        services.AddScoped<IAccountAuthorityLifecycleEmailService, DefaultAccountAuthorityLifecycleEmailService>();
        services.AddScoped<IKeycloakRealmDesiredStateBuilder, KeycloakRealmDesiredStateBuilder>();
        services.AddScoped<IAnalyticsGovernanceService, AnalyticsGovernanceService>();
        services.AddScoped<
            IPaidEventPolicyMutationBoundary,
            PaidEventPolicyMutationBoundary>();
        services.AddScoped<ILocationPrivacyGovernanceService, LocationPrivacyGovernanceService>();
        services.AddScoped<ILocationPrivacyGovernanceMutationService, LocationPrivacyGovernanceMutationService>();
        services.AddScoped<IAddressGovernancePolicyResolver, AddressGovernancePolicyResolver>();
        services.AddScoped<EventLocationAttachmentService>();
        services.AddSingleton<IEventLocationRegistrationAccessService, EventLocationRegistrationAccessService>();
        services.AddSingleton<IFormSchemaArtifactGenerator, FormSchemaArtifactGenerator>();
        services.AddSingleton<FormSchemaArtifactPublicationService>();
        services.AddSingleton<SchemaDriftClassifier>();
        services.AddScoped<RegistrationEffectiveCapabilityResolver>();
        services.AddScoped<IRegistrationProviderManagedPublishPreflight, RegistrationProviderManagedPublishPreflightService>();
        services.AddScoped<IRegistrationProviderConnectionCheckpoint, RegistrationProviderConnectionCheckpointService>();
        services.AddScoped<RegistrationProviderSubscriptionLifecycleService>();
        services.AddScoped<OrganizerPaymentReadinessReconciliationService>();
        services.AddScoped<PaidEventPublicationPreflightService>();
        services.AddScoped<PaidCheckoutActivationService>();
        services.AddSingleton<IPaidCheckoutTelemetry, PaidCheckoutTelemetry>();
        services.AddScoped<IPaidCheckoutActivationService, TelemetryPaidCheckoutActivationService>();
        services.AddScoped<IPaidOrderAcceptanceService, PaidOrderAcceptanceService>();
        services.AddScoped<IPaidOrderAcceptanceFreshnessService, PaidOrderAcceptanceFreshnessService>();
        services.AddSingleton(provider => new RegistrationFormPublishPreflightService(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RegistrationFileAnswerOptions>>().Value));
        services.AddScoped<RegistrationFormAuthoringCommandService>();
        services.AddScoped<RegistrationFormTemplateCommandService>();
        services.AddScoped<IEventLocationExactReadAuditService, EventLocationExactReadAuditService>();
        services.AddScoped<IEventLocationManagementAuthorizationService, EventLocationManagementAuthorizationService>();
        services.AddSingleton<EventLocationPrivacyMetrics>();
        services.AddScoped<IEventLocationDisclosureService, EventLocationDisclosureService>();
        services.AddScoped<IEventLocationReviewQueueMonitor, EventLocationReviewQueueMonitor>();
        services.AddScoped<IFanoutAttendeeLocationAuthorizationService, FanoutAttendeeLocationAuthorizationService>();
        services.AddScoped<PrivacyErasureApplier>();
        services.AddScoped<IPrivacyErasureService, RetainedAuthorityPrivacyErasureWorkflow>();
        services.AddSingleton<EventLocationDisclosureEvaluator>();
        services.AddScoped<PublicEventLocationDisclosureEvaluator>();
        services.AddScoped<AtprotoEventPublicationSnapshotFactory>();
        services.AddScoped<AtprotoEventGovernanceResolver>();
        services.AddScoped<AtprotoEventPublicationPlanner>();
        services.AddScoped<IAtprotoDeliveryGate>(provider =>
            provider.GetRequiredService<AtprotoEventPublicationPlanner>());
        services.AddScoped<IAtprotoLocationPrivacyCorrectionPlanner>(provider =>
            provider.GetRequiredService<AtprotoEventPublicationPlanner>());
        services.AddScoped<AtprotoPdsDeliveryProcessor>();
        services.AddScoped<IModuleCapabilityService, ModuleCapabilityService>();
        services.AddScoped<SettingUpsertService>();
        services.AddScoped<ManagedTenantProvisioningPreflight>();
        services.AddScoped<ManagedTenantProvisioningCapacityReader>();
        services.AddScoped<TenantActivationCapacityPolicy>();
        services.AddScoped<TenantPlanStorageQuotaCeilingPolicy>();
        services.AddScoped<IManagedProviderClientProvisioner, EnsureManagedProviderClientProvisionedCommandHandler>();
        services.AddScoped<Features.SetupLive.SetupLiveApplicationService>();
        services.AddSingleton<Telemetry.SetupLiveTelemetry>();

        // Analytics consent / runtime profile resolution
        services.AddScoped<IAnalyticsRuntimeProfileResolver, AnalyticsRuntimeProfileResolver>();
        services.AddScoped<IStoragePolicyResolver, StoragePolicyResolver>();
        services.AddScoped<IStorageObjectContentReader, StorageObjectContentReader>();

        // Authorization: dynamic permission infrastructure
        services.AddScoped<ICapabilityCeilingService, CapabilityCeilingService>();
        services.AddScoped<IEventRoleAuthorityCeilingService, EventRoleAuthorityCeilingService>();
        services.AddScoped<ICustomPropertyGovernancePolicy, CustomPropertyGovernancePolicy>();
        services.AddScoped<ICustomPropertyAutomationConditionPolicy, CustomPropertyAutomationConditionPolicy>();
        services.AddScoped<IEventActorResolver, EventActorResolver>();
        services.AddScoped<
            ITicketPurchaseAuthorityResolver,
            TicketPurchaseAuthorityResolver>();
        services.AddScoped<
            ITicketPurchaseOrderResolver,
            TicketPurchaseOrderResolver>();
        services.AddScoped<TicketTypeEntitlementResolver>();
        services.AddScoped<IEventTemplateInstantiationService, EventTemplateInstantiationService>();
        services.AddScoped<IEventSessionTemplateInstantiationService, EventSessionTemplateInstantiationService>();
        services.AddScoped<IEventTemplateDiffService, EventTemplateDiffService>();
        services.AddScoped<IEventTemplateSyncService, EventTemplateSyncService>();
        services.AddScoped<IEventSessionTemplateDiffService, EventSessionTemplateDiffService>();
        services.AddScoped<IEventSessionTemplateSyncService, EventSessionTemplateSyncService>();
        services.AddScoped<IPermissionRegistryService, PermissionRegistryService>();
        services.AddScoped<IContactShareConsentService, ContactShareConsentService>();
        services.AddScoped<INotificationOwnershipResolver, DefaultNotificationOwnershipResolver>();
        services.AddScoped<INotificationOrchestrator, DefaultNotificationOrchestrator>();
        services.AddScoped<IRecipientNotificationMaterializer, RecipientNotificationMaterializer>();
        services.AddSingleton<NotificationDeliveryPolicyResolver>();
        services.AddSingleton<ReportReceiptNotificationFactory>();
        services.AddSingleton<ReportOutcomeNotificationFactory>();
        services.AddSingleton<ReportNeedsMoreInformationNotificationFactory>();
        services.AddSingleton<EventOrganizerWarningNotificationFactory>();
        services.AddSingleton<NotificationFanoutRecipientTemplateFactory>();
        services.AddScoped<NotificationFanoutRecipientMaterializationService>();
        services.AddScoped<INotificationFanoutRecipientMaterializationService>(provider =>
            provider.GetRequiredService<NotificationFanoutRecipientMaterializationService>());
        services.AddScoped<NotificationFanoutPageProcessor>();
        services.AddScoped<NotificationFanoutOccurrenceCoordinator>();
        services.AddScoped<NotificationFanoutOccurrenceHandoffService>();
        services.AddScoped<IEventPublishedNotificationFanoutService, EventPublishedNotificationFanoutService>();
        services.AddScoped<IEventModerationNotificationFanoutService, EventModerationNotificationFanoutService>();
        services.AddScoped<IEventDetailsProjectionService, EventDetailsProjectionService>();
        services.AddScoped<INotificationRefreshStreamService, NotificationRefreshStreamService>();
        services.AddScoped<IEventLifecycleScheduler, EventLifecycleScheduler>();
        services.AddScoped<RegistrationOrderLifecycleService>();
        services.AddScoped<IRegistrationOrderTransitionCoordinator, RegistrationOrderTransitionCoordinator>();
        services.AddScoped<RegistrationPaymentAttemptClaimService>();
        services.AddScoped<RegistrationPaymentContractService>();
        services.AddScoped<RegistrationPaymentCheckoutDispatchService>();
        services.AddScoped<RegistrationPaymentReconciliationService>();
        services.AddScoped<RefundDispatchService>();
        services.AddScoped<RefundReconciliationService>();
        services.AddScoped<
            FairReturnOrchestrationService>();
        services.AddScoped<
            IFairReturnOrchestrationDispatcher>(
                provider => provider
                    .GetRequiredService<
                        FairReturnOrchestrationService>());
        services.AddScoped<RefundCampaignProcessor>();
        services.AddScoped<RegistrationRefundService>();
        services.AddScoped<RegistrationMaterialChangeChoiceService>();
        services.AddScoped<RegistrationPaymentCancellationService>();
        services.AddScoped<RegistrationParticipantCommandService>();
        services.AddScoped<IRegistrationOrderLifecycleService>(provider => provider.GetRequiredService<RegistrationOrderLifecycleService>());
        services.AddScoped<IRegistrationOrderStarter, CreateOrderWithHoldCommandHandler>();
        services.AddScoped<AtprotoEventGovernanceResolver>();
        services.AddScoped<AtprotoJetstreamTenantPresentationResolver>();
        services.AddScoped<AtprotoPdsRecoveryPolicyResolver>();
        services.AddScoped<IEventLifecyclePolicyProvider, EventLifecyclePolicyProvider>();
        services.AddScoped<IEventLifecycleReadinessEvaluator, EventLifecycleReadinessEvaluator>();
        services.AddScoped<IEventReportingIntakeGuard, EventReportingIntakeGuard>();
        services.AddScoped<EventPublicationExecutor>();
        services.AddScoped<IScheduledDeadlineDispatcher, NoOpScheduledDeadlineDispatcher>();
        services.AddSingleton<IScheduledJobRegistry, ScheduledJobRegistry>();
        services.AddSingleton<IWebhookEventTypeRegistry, WebhookEventTypeRegistry>();
        services.AddSingleton<IWebhookEventSchemaProvider, WebhookEventSchemaProvider>();
        services.AddSingleton<IWebhookPayloadBuilder, DefaultWebhookPayloadBuilder>();
        services.AddSingleton<IWebhookDeliveryPlanResolver, FailClosedWebhookDeliveryPlanResolver>();
        services.AddScoped<IWebhookEventTypeCatalogSyncService, WebhookEventTypeCatalogSyncService>();
        services.AddScoped<IWebhookEventPublisher, DefaultWebhookEventPublisher>();

        // Scheduling domain services (stateless, safe as singleton).
        services.AddSingleton<IEventScheduleProjectionCalculator, EventScheduleProjectionCalculator>();
        services.AddSingleton<IOrganizerEarningsCalculator, OrganizerEarningsCalculator>();

        // Appearance resolution and palette generation
        services.AddScoped<IAppearanceResolutionService, AppearanceResolutionService>();

        return services;
    }
}
