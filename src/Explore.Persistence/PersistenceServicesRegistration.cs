using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Recovery;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.SetupLive;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Domain;
using Explore.Persistence.Caching;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Queries;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Bootstrap;
using Explore.Secrets.Database;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Persistence;

public static class PersistenceServicesRegistration
{
    // didn't implement aspire integration trough passing the builder cause it will require to install aspnetcore nuget package in persistence project and i want to keep it clean so let this dependency in API project only
    //public static IServiceCollection ConfigurePersistenceServices(this IServiceCollection services,
    //    WebApplicationBuilder builder) // Pass the builder instead of just configuration
    //{
    //    // Use Aspire's integration
    //    builder.AddNpgsqlDbContext<ExploreDbContext>("ExploreDB");

    /// <summary>
    /// Composes provider-specific persistence and scoped repositories, binding database and privacy authorities from configured sources.
    /// </summary>
    public static IServiceCollection ConfigurePersistenceServices(this IServiceCollection services,
        IConfiguration configuration,
        bool skipDbContextRegistration = false,
        bool skipLookupCacheInitializer = false,
        string? environmentName = null)
    {
        PrivacyErasureDurabilityOptions erasureDurability =
            PrivacyErasureDurabilityOptions.FromConfiguration(configuration);

        string? applicationConnectionString = null;
        PrimaryDatabaseProvider? applicationProvider = null;
        PrimaryDatabaseConnectionOptions applicationRuntimeOptions = new()
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql
        };

        // Skip DbContext registration when running integration tests (they register their own)
        if (!skipDbContextRegistration || erasureDurability.Topology == PrivacyErasureAuthorityTopology.CoLocated)
        {
            var runtimeDatabaseOptions = PrimaryDatabaseConfiguration.BindRuntime(configuration);
            var runtimeDatabase = PrimaryDatabaseConfiguration.BuildConnectionString(runtimeDatabaseOptions);
            applicationConnectionString = runtimeDatabase.ConnectionString;
            applicationProvider = runtimeDatabase.Provider;
            applicationRuntimeOptions = runtimeDatabaseOptions;

            services.AddDbContext<DataProtectionKeyContext>(options =>
                PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, runtimeDatabaseOptions));

            // Use pooled DbContext factory for performance (EF Core recommended pattern)
            // The scoped ExploreDbContext registration below handles scoped dependency injection
            services.AddPooledDbContextFactory<ExploreDbContext>(options =>
            {
                PrimaryDatabaseProviderComposition.ConfigureApplication(options, runtimeDatabaseOptions);

                if (runtimeDatabase.Provider == PrimaryDatabaseProvider.PostgreSql
                    && IsEnabled(configuration["Persistence:EnableRlsTenantSession"]))
                {
                    options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
                }

                var runtimeEnvironmentName = environmentName
                                             ?? configuration["ASPNETCORE_ENVIRONMENT"]
                                             ?? configuration["DOTNET_ENVIRONMENT"];
                if (string.Equals(runtimeEnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    options.EnableDetailedErrors();
                    options.ConfigureWarnings(warnings =>
                        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
                }
            });

            // Register scoped DbContext that sets scoped dependencies from DI
            // This follows EF Core's recommended pattern for pooled contexts with scoped dependencies
            services.AddScoped(sp =>
            {
                var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
                var context = factory.CreateDbContext();

                // Set scoped dependencies via property injection (null during migrations, populated during API requests)
                context.ClearTenantFilterBypass();
                context.TenantContext = sp.GetService<ITenantContext>();
                context.CurrentUserService = sp.GetService<ICurrentUserService>();

                return context;
            });

        }

        LocalIdentityOptions localIdentityOptions =
            configuration.GetSection(LocalIdentityOptions.SectionName).Get<LocalIdentityOptions>()
            ?? new LocalIdentityOptions();
        IdentityBuilder identityBuilder = services.AddIdentityCore<LocalIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = localIdentityOptions.LockoutThreshold;
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(localIdentityOptions.LockoutDurationMinutes);
                options.Password.RequiredLength = LocalIdentityOptions.MinimumPasswordLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
            })
            .AddRoles<LocalIdentityRole>()
            .AddUserValidator<OptionalEmailLocalIdentityUserValidator>();

        IdentityDatabaseTopology identityTopology = IdentityDatabaseConfiguration.GetTopology(configuration);
        if (identityTopology == IdentityDatabaseTopology.External)
        {
            services.AddDbContext<ExternalIdentityDbContext>(options =>
                IdentityDatabaseProviderComposition.Configure(
                    options,
                    configuration,
                    PrimaryDatabaseRole.Runtime));
            identityBuilder.AddEntityFrameworkStores<ExternalIdentityDbContext>();
        }
        else
        {
            identityBuilder.AddEntityFrameworkStores<ExploreDbContext>();
        }
        identityBuilder.AddDefaultTokenProviders().AddLocalLifecycleTokenProviders();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped(serviceProvider =>
        {
            DbContext identityDbContext = identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>();
            return new LocalIdentityCredentialStateStore(
                identityDbContext: identityDbContext,
                applicationDbContext: serviceProvider.GetRequiredService<ExploreDbContext>(),
                userManager: serviceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
                timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
                identityFence: serviceProvider.GetRequiredService<Explore.Application.Services.PrivacyIdentityFenceOperation>());
        });
        services.AddScoped<ILocalIdentityLifecycleStore>(serviceProvider => new LocalIdentityLifecycleStore(
            identityDbContext: identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>(),
            applicationDbContext: serviceProvider.GetRequiredService<ExploreDbContext>(),
            userManager: serviceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
            timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
            credentialStates: serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>(),
            identityFence: serviceProvider.GetRequiredService<Explore.Application.Services.PrivacyIdentityFenceOperation>()));
        services.AddScoped<ILocalIdentityLifecycleDeliveryStore>(serviceProvider => new LocalIdentityLifecycleDeliveryStore(
            identityDbContext: identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>(),
            applicationDbContext: serviceProvider.GetRequiredService<ExploreDbContext>(),
            credentialStates: serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>(),
            timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
            identityFence: serviceProvider.GetRequiredService<Explore.Application.Services.PrivacyIdentityFenceOperation>()));
        services.AddScoped<ILocalIdentityAuthService, LocalIdentityAuthService>();
        services.AddScoped<ILocalCredentialAdministration>(serviceProvider =>
            serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>());

        // Unit of Work (wraps EF Core transactions)
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<IModerationAuthoritySourceRepository, ModerationAuthoritySourceRepository>();
        services.AddScoped<IModerationMachineSubjectBindingRepository, ModerationMachineSubjectBindingRepository>();
        services.AddScoped<IModerationProfileCollectionAssessmentRepository, ModerationProfileCollectionAssessmentRepository>();
        services.AddScoped<IModerationProfileCollectionSubjectObservationOwner, ModerationProfileCollectionSubjectObservationOwner>();
        services.AddScoped<IModerationProfileSubjectConsentOwner, ModerationProfileSubjectConsentOwner>();
        services.AddScoped<IModerationProfileCollectionAdmissionOwner, ModerationProfileCollectionAdmissionOwner>();
        services.AddScoped<Explore.Application.Contracts.Scheduling.IModerationReconciliationQuery,
            Explore.Persistence.Queries.ModerationReconciliationQuery>();
        services.AddScoped<IEventResourceModerationSnapshotReader, EventResourceModerationSnapshotReader>();
        services.AddScoped<IEventResourceUploadMutationBoundary, EventResourceUploadMutationBoundary>();
        services.AddScoped<IEventPublicationMutationBoundary, EventPublicationMutationBoundary>();
        services.AddScoped<ITenantUserRoleGrantMutationBoundary, TenantUserRoleGrantMutationBoundary>();
        services.AddScoped<OrganizationMemberMutationBoundary>();
        services.AddScoped<IOrganizationMemberMutationBoundary>(sp => sp.GetRequiredService<OrganizationMemberMutationBoundary>());
        services.AddScoped<IOrganizationCreationMutationBoundary>(sp => sp.GetRequiredService<OrganizationMemberMutationBoundary>());
        services.AddScoped<GroupMemberMutationBoundary>();
        services.AddScoped<IGroupMemberMutationBoundary>(provider => provider.GetRequiredService<GroupMemberMutationBoundary>());
        services.AddScoped<IGroupCreationMutationBoundary>(provider => provider.GetRequiredService<GroupMemberMutationBoundary>());
        services.AddScoped<EventRoleAssignmentMutationBoundary>();
        services.AddScoped<IEventOwnershipTransferMutationBoundary>(provider => provider.GetRequiredService<EventRoleAssignmentMutationBoundary>());
        services.AddScoped<IEventRoleAssignmentWindowMutationBoundary>(provider => provider.GetRequiredService<EventRoleAssignmentMutationBoundary>());
        services.AddScoped<IEventRoleAssignmentRevocationMutationBoundary>(provider => provider.GetRequiredService<EventRoleAssignmentMutationBoundary>());
        services.AddScoped<IEventRoleAssignmentCreationMutationBoundary>(provider => provider.GetRequiredService<EventRoleAssignmentMutationBoundary>());
        services.AddScoped<IEventInitialCreationMutationBoundary>(provider => provider.GetRequiredService<EventRoleAssignmentMutationBoundary>());
        services.AddScoped<IAtprotoParticipationMutationFactory, AtprotoParticipationMutationFactory>();
        services.AddScoped<ModerationQualificationRepository>();
        services.AddScoped<IModerationQualificationRepository>(provider =>
            provider.GetRequiredService<ModerationQualificationRepository>());
        services.AddScoped<IModerationContentResponseRepository>(provider =>
            provider.GetRequiredService<ModerationQualificationRepository>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationPreFindingRepository>(provider =>
            provider.GetRequiredService<ModerationQualificationRepository>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationPreFindingResponseOwner>(provider =>
            provider.GetRequiredService<ModerationQualificationRepository>());
        services.AddScoped<IModerationEffectLeaseRepository, ModerationEffectLeaseRepository>();
        services.AddScoped<IModerationResourceQuarantineRepository, ModerationResourceQuarantineRepository>();
        services.AddScoped<IModerationOriginalEffectCompletionRepository, ModerationOriginalEffectCompletionRepository>();
        services.AddScoped<ModerationResourceQuarantineReleaseRepository>();
        services.AddScoped<IModerationResourceQuarantineReleaseRepository>(provider =>
            provider.GetRequiredService<ModerationResourceQuarantineReleaseRepository>());
        services.AddScoped<IModerationResourceQuarantineInspectionRepository, ModerationResourceQuarantineInspectionRepository>();
        services.AddScoped<ModerationResourceQuarantineStateRepository>();
        services.AddScoped<IModerationResourceQuarantineStateRepository>(provider =>
            provider.GetRequiredService<ModerationResourceQuarantineStateRepository>());
        services.AddScoped<IModerationResourceQuarantineReviewStateRepository>(provider =>
            provider.GetRequiredService<ModerationResourceQuarantineStateRepository>());
        services.AddScoped<Explore.Application.Services.ModerationResourceInspectionMaterialReader>();
        services.AddScoped<ModerationResourceQuarantineDutyRepository>();
        services.AddScoped<IModerationResourceQuarantineDutyRepository>(provider =>
            provider.GetRequiredService<ModerationResourceQuarantineDutyRepository>());
        services.AddScoped<IModerationScheduleRiskReviewReconciliationRepository>(provider =>
            provider.GetRequiredService<ModerationResourceQuarantineDutyRepository>());
        services.AddScoped<Explore.Application.Services.Moderation.ModerationResourceReviewEscalationMaterializer>();
        services.AddScoped<ModerationRecognitionErasureRepository>();
        services.AddScoped<IModerationRecognitionErasureRepository>(provider =>
            provider.GetRequiredService<ModerationRecognitionErasureRepository>());
        services.AddScoped<IModerationRecognitionContinuityRepository>(provider =>
            provider.GetRequiredService<ModerationRecognitionErasureRepository>());
        services.AddScoped<IModerationRecognitionRecoveryRepository>(provider =>
            provider.GetRequiredService<ModerationRecognitionErasureRepository>());
        services.AddSingleton<ModerationRecognitionKeyLifecycleReplicaOwner>();
        services.AddScoped<ModerationRecognitionKeyLifecycleRepository>();
        services.AddScoped<IModerationRecognitionKeyLifecycleRepository>(provider =>
            provider.GetRequiredService<ModerationRecognitionKeyLifecycleRepository>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationRecognitionKeyLifecycleGuard>(provider =>
            provider.GetRequiredService<ModerationRecognitionKeyLifecycleRepository>());
        services.AddScoped<IModerationCaseTargetReceiptRepository, ModerationCaseTargetReceiptRepository>();
        services.AddScoped<ModerationCaseTargetProofOwner>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseTargetProofOwner>(provider =>
            provider.GetRequiredService<ModerationCaseTargetProofOwner>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseTargetIssuer>(provider =>
            provider.GetRequiredService<ModerationCaseTargetProofOwner>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseAttributionOwner>(provider =>
            provider.GetRequiredService<ModerationCaseTargetProofOwner>());
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseAttributionRepository,
            ModerationCaseAttributionRepository>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseReadGrantRepository,
             ModerationCaseReadGrantRepository>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationAdjudicatedCaseReadGrantRepository,
            ModerationAdjudicatedCaseReadGrantRepository>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseIntakeRepository, ModerationCaseIntakeRepository>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseNarrativeCustody, ModerationCaseNarrativeRepository>();
        services.AddScoped<Explore.Application.Contracts.Moderation.IModerationCaseIntakeCommitter,
            Explore.Persistence.Moderation.ModerationCaseIntakeCommitter>();
        services.AddScoped<ModerationPolicyDefinitionSourceOwner>();
        services.AddScoped<ModerationDeploymentSourceOwner>();
        services.AddScoped<ModerationConstitutionSourceOwner>();
        services.AddScoped<ModerationRecognitionPrivacySourceOwner>();
        services.AddScoped<ModerationChildAgePolicySourceOwner>();
        services.AddScoped<ModerationClassificationTaxonomySourceOwner>();
        services.AddScoped<IModerationClassificationTaxonomySourceOwner>(provider =>
            provider.GetRequiredService<ModerationClassificationTaxonomySourceOwner>());
        services.AddScoped<ModerationClassificationMaterialSourceOwner>();
        services.AddScoped<IModerationClassificationMaterialSourceOwner>(provider =>
            provider.GetRequiredService<ModerationClassificationMaterialSourceOwner>());
        services.AddScoped<IModerationPublicEventCalendarRepository, ModerationPublicEventCalendarRepository>();
        services.AddScoped<ModerationEventRightsSourceOwner>();
        services.AddScoped<ModerationProfileDeploymentSourceOwner>();
        services.AddScoped<ModerationMeasuredOperationsSourceOwner>();
        services.AddScoped<IModerationActivationSourceOwner, ModerationActivationSourceOwner>();
        services.AddScoped<IModerationPolicyPublicationRepository>(provider =>
            new ModerationPolicyPublicationRepository(
                provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<IAdminContext>(),
                provider.GetRequiredService<IMachinePrincipalAccessor>(),
                provider.GetRequiredService<Explore.Application.Services.Moderation.ModerationCapabilityRegistry>(),
                provider.GetServices<IModerationPolicyPublicationEvidenceOwner>().SingleOrDefault(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetServices<IModerationConsumerRuntimeSource>(),
                provider.GetService<IModerationMeasurementReceiptVerifier>()));
        services.AddScoped<IModerationClassificationMaterialInventoryReadRepository>(provider =>
            (IModerationClassificationMaterialInventoryReadRepository)provider.GetRequiredService<IModerationPolicyPublicationRepository>());
        services.AddScoped<IModerationClassificationMaterialReviewReadRepository>(provider =>
            (IModerationClassificationMaterialReviewReadRepository)provider.GetRequiredService<IModerationPolicyPublicationRepository>());
        services.AddScoped<IModerationPolicyPublicationSelectionSource, ModerationPolicySelectionSource>();
        services.AddScoped<IModerationPolicyPublicationNoticeAuthority>(provider =>
            provider.GetRequiredService<ModerationAuthorityParticipant>());
        services.AddScoped<IModerationNoticeSourceOwner, ModerationPolicySelectionNoticeSourceOwner>();
        services.AddScoped<ModerationNoticeRecipientRepository>();
        services.AddScoped<IModerationNoticeCleanupRepository, ModerationNoticeCleanupRepository>();
        services.AddScoped<Explore.Application.Contracts.Notifications.IModerationNoticeStatusRepository, ModerationNoticeStatusRepository>();
        services.AddScoped<ModerationNoticeReadRepository>();
        services.AddScoped<Explore.Application.Contracts.Notifications.IModerationNoticeCorrectionDisclosure>(provider =>
            provider.GetRequiredService<ModerationNoticeReadRepository>());
        services.AddScoped<Explore.Application.Contracts.Notifications.IModerationNoticeEmailAuthority,
            ModerationNoticeEmailAuthority>();
        services.AddScoped<Explore.Application.Contracts.Notifications.IModerationNoticeReadRepository>(provider =>
            provider.GetRequiredService<ModerationNoticeReadRepository>());
        services.AddScoped<Explore.Application.Contracts.Notifications.IModerationNoticeNotificationRepository>(provider =>
            provider.GetRequiredService<ModerationNoticeReadRepository>());
        services.AddScoped<IModerationNoticeCapture>(provider => new ModerationNoticeCaptureRepository(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<IPrivacyIdentityFenceAuthority>(),
            provider.GetServices<IModerationNoticeSourceOwner>().SingleOrDefault(),
            provider.GetRequiredService<IModerationPolicyPublicationSelectionSource>(),
            provider.GetRequiredService<ModerationNoticeRecipientRepository>(),
            provider.GetRequiredService<Explore.Application.Services.Moderation.ModerationNoticeNativeMaterializer>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IModerationAuthorityParticipant>()));
        services.AddScoped<IModerationQualificationPolicySourceOwner, ModerationQualificationPolicySourceOwner>();
        services.AddScoped(provider => new ModerationNativeActivationQualification(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<Explore.Application.Services.Moderation.ModerationCapabilityRegistry>(),
            provider.GetServices<IModerationActivationSourceOwner>().SingleOrDefault(),
            provider.GetServices<IModerationQualificationPolicySourceOwner>().SingleOrDefault(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ModerationAuthorityParticipant>();
        services.AddScoped<IModerationAuthorityParticipant>(provider =>
            provider.GetRequiredService<ModerationAuthorityParticipant>());
        services.AddScoped<IModerationAuthorityContext>(provider =>
            provider.GetRequiredService<ModerationAuthorityParticipant>());
        services.AddScoped<IModerationStore>(provider => new ModerationStore(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<IModerationAuthorityParticipant>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IPrivacyIdentityFenceAuthority>(),
            provider.GetRequiredService<IModerationNoticeCapture>()));
        services.AddScoped<Explore.Application.Contracts.Services.Registration.IAnonymousRegistrationChallengeQuota,
            AnonymousRegistrationChallengeQuota>();
        services.AddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.AddScoped<ICoordinatedSettingMutationStore, CoordinatedSettingMutationRepository>();
        services.AddScoped<IAtprotoSessionRefreshLock, RelationalAtprotoSessionRefreshLock>();
        services.AddScoped<ISetupLiveRepository, SetupLiveRepository>();
        services.AddScoped<IKeycloakOperationRepository, KeycloakOperationRepository>();
        services.AddScoped<IKeycloakOperationCoordinator, RelationalKeycloakOperationCoordinator>();
        services.AddScoped<ISetupSecretBindingOperationCoordinator,
            RelationalSetupSecretBindingOperationCoordinator>();

        services.AddScoped<IGenericRepository<EventReportDecision, Guid>, GenericRepository<EventReportDecision, Guid>>();
        services.AddScoped<IGenericRepository<EventReportTarget, Guid>, GenericRepository<EventReportTarget, Guid>>();
        services.AddScoped<IGenericRepository<EventReportEvidence, Guid>, GenericRepository<EventReportEvidence, Guid>>();
        services.AddScoped<IGenericRepository<EventReportCase, Guid>, GenericRepository<EventReportCase, Guid>>();
        services.AddScoped<IGenericRepository<UserPii, Guid>, GenericRepository<UserPii, Guid>>();
        services.AddScoped<IGenericRepository<ActorPii, Guid>, GenericRepository<ActorPii, Guid>>();
        services.AddScoped<IGenericRepository<ActorMerge, Guid>, GenericRepository<ActorMerge, Guid>>();

        // Lookup cache
        services.AddSingleton<ILookupDataCache, LookupDataCache>();
        if (!skipLookupCacheInitializer)
        {
            services.AddHostedService<LookupDataCacheInitializer>();
        }

        // Lookup Table Repositories
        services.AddScoped<IApprovalStatusRepository, ApprovalStatusRepository>();
        services.AddScoped<IAudienceAgeRepository, AudienceAgeRepository>();
        services.AddScoped<IAudienceGenderRepository, AudienceGenderRepository>();
        services.AddScoped<IEventTypeRepository, EventTypeRepository>();
        services.AddScoped<IEventStatusRepository, EventStatusRepository>();
        services.AddScoped<IEventSessionStatusRepository, EventSessionStatusRepository>();
        services.AddScoped<IEventFormatRepository, EventFormatRepository>();
        services.AddScoped<IVisibilityTypeRepository, VisibilityTypeRepository>();
        services.AddScoped<IRegistrationModeRepository, RegistrationModeRepository>();
        services.AddScoped<IEventRegistrationPolicyRepository, EventRegistrationPolicyRepository>();
        services.AddScoped<IRegistrationScopeRepository, RegistrationScopeRepository>();
        services.AddScoped<IEventSessionKindRepository, EventSessionKindRepository>();
        services.AddScoped<IScheduleItemKindRepository, ScheduleItemKindRepository>();
        services.AddScoped<IMadhabRepository, MadhabRepository>();
        services.AddScoped<ILanguageRepository, LanguageRepository>();
        services.AddScoped<IOrganizationPositionRepository, OrganizationPositionRepository>();
        services.AddScoped<IGroupPositionRepository, GroupPositionRepository>();
        services.AddScoped<IActorTypeRepository, ActorTypeRepository>();
        services.AddScoped<IDidCustodyTypeRepository, DidCustodyTypeRepository>();
        services.AddScoped<IFileTypeRepository, FileTypeRepository>();
        services.AddScoped<INotificationTypeRepository, NotificationTypeRepository>();
        services.AddScoped<INotificationEntityTypeRepository, NotificationEntityTypeRepository>();

        // Multi-tenancy Repositories
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantLookupSource, TenantLookupSource>();
        services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
        services.AddScoped<IPlatformUserRoleRepository, PlatformUserRoleRepository>();
        services.AddScoped<ITenantUserRepository, TenantUserRepository>();
        services.AddScoped<ITenantUserProfileRepository, TenantUserProfileRepository>();
        services.AddScoped<ITenantUserRoleGrantRepository, TenantUserRoleGrantRepository>();
        services.AddScoped<ITenantOnboardingStateRepository, TenantOnboardingStateRepository>();
        services.AddScoped<ITenantNavigationLinkRepository, TenantNavigationLinkRepository>();
        services.AddScoped<IFooterLinkGroupRepository, FooterLinkGroupRepository>();
        services.AddScoped<IFooterLinkRepository, FooterLinkRepository>();
        services.AddScoped<ITenantInvitationRepository, TenantInvitationRepository>();
        services.AddScoped<ITenantLifecycleLogRepository, TenantLifecycleLogRepository>();
        services.AddScoped<ITenantPlanRepository, TenantPlanRepository>();

        // User & Authentication Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IExternalApiKeyRepository, ExternalApiKeyRepository>();
        services.AddScoped<IExternalApiKeyIssuanceReceiptRepository, ExternalApiKeyIssuanceReceiptRepository>();
        services.AddScoped<IExternalApiKeyIssuanceAuthority, ExternalApiKeyIssuanceAuthority>();
        services.AddScoped<IManagedControlPlaneRegistrationRepository, ManagedControlPlaneRegistrationRepository>();
        services.AddScoped<IManagedTenantProvisioningOperationRepository, ManagedTenantProvisioningOperationRepository>();
        services.AddScoped<IConfigurationManifestOperationRepository, ConfigurationManifestOperationRepository>();
        services.AddScoped<IConfigurationManifestFailureRecorder, ConfigurationManifestFailureRepository>();
        services.AddScoped<ILegalDocumentRepository, LegalDocumentRepository>();
        services.AddScoped<IExternalApiKeyQuotaRepository, ExternalApiKeyQuotaRepository>();
        services.AddScoped<IUserNotificationPreferenceRepository, UserNotificationPreferenceRepository>();
        services.AddScoped<IUserAuthenticationTokenRepository, UserAuthenticationTokenRepository>();
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddScoped<IUserIdentityEmailRepository, UserIdentityEmailRepository>();
        services.AddScoped<IExternalBindingRepository, ExternalBindingRepository>();

        // Actor Repositories
        services.AddScoped<IActorRepository, ActorRepository>();
        services.AddScoped<IAtprotoIdentityRepository, AtprotoIdentityRepository>();
        services.AddScoped<IActorReferenceConsolidationRepository, ActorReferenceConsolidationRepository>();
        services.AddScoped<IActorKeyStoreRepository, ActorKeyStoreRepository>();
        services.AddScoped<IActorSubscriptionRepository, ActorSubscriptionRepository>();
        services.AddScoped<INotificationFanoutRunRepository, NotificationFanoutRunRepository>();
        services.AddScoped<INotificationFanoutOccurrenceRepository, NotificationFanoutOccurrenceRepository>();
        services.AddScoped<INotificationFanoutEmailSuppressionRepository, NotificationFanoutEmailSuppressionRepository>();

        // Organization Repositories
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IOrganizationTenantRepository, OrganizationTenantRepository>();
        services.AddScoped<IOrganizationMemberRepository, OrganizationMemberRepository>();
        services.AddScoped<IOrganizationReviewRepository, OrganizationReviewRepository>();

        // Group Repositories
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IGroupTenantRepository, GroupTenantRepository>();
        services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
        services.AddScoped<ICustomPropertyDefinitionRepository, CustomPropertyDefinitionRepository>();

        // Event Repositories
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IEventParticipationConfigurationRepository, EventParticipationConfigurationRepository>();
        services.AddScoped<IRegistrationFormAuthoringRepository, RegistrationFormAuthoringRepository>();
        services.AddScoped<IRegistrationFormTemplateRepository, RegistrationFormTemplateRepository>();
        services.AddScoped<IRegistrationProviderRepository, RegistrationProviderRepository>();
        services.AddScoped<IRegistrationProviderSubscriptionStateRepository, RegistrationProviderSubscriptionStateRepository>();
        services.AddScoped<IParticipationRequirementAttachmentRepository, ParticipationRequirementAttachmentRepository>();
        services.AddScoped<IEventTicketCatalogRepository, EventTicketCatalogRepository>();
        services.AddScoped<IRegistrationInventoryRepository, RegistrationInventoryRepository>();
        services.AddScoped<IGuestRegistrationCapabilityRepository, GuestRegistrationCapabilityRepository>();
        services.AddScoped<IEventAddOnRepository, EventAddOnRepository>();
        services.AddScoped<TicketingRecoveryRepository>();
        services.AddScoped<
            ITicketingRecoveryOperatorStore,
            TicketingRecoveryRepository>();
        services.AddScoped<IPromotionManagementRepository, PromotionManagementRepository>();
        services.AddScoped<IPromotionRedemptionRepository, PromotionRedemptionRepository>();
        services.AddScoped<IRegistrationParticipantRepository, RegistrationParticipantRepository>();
        services.AddScoped<IRegistrationSubmissionRepository, RegistrationSubmissionRepository>();
        services.AddScoped<IRegistrationAnswerAnalyticsRepository, RegistrationAnswerAnalyticsRepository>();
        services.AddScoped<IRegistrationRetentionCleanupRepository, RegistrationRetentionCleanupRepository>();
        services.AddScoped<IRegistrationAnswerFileRepository, RegistrationAnswerFileRepository>();
        services.AddScoped<IRegistrationFinalizationRepository, RegistrationFinalizationRepository>();
        services.AddScoped<IAdmissionTargetMaterializationRepository, AdmissionTargetMaterializationRepository>();
        services.AddScoped<IAdmissionIssuanceRepository, AdmissionIssuanceRepository>();
        services.AddScoped<ParticipantAdmissionEligibilityRepository>();
        services.AddScoped<IParticipantAdmissionEligibilityRepository>(
            provider => provider.GetRequiredService<
                ParticipantAdmissionEligibilityRepository>());
        services.AddScoped<IParticipantAdmissionReadinessAuthority>(
            provider => provider.GetRequiredService<
                ParticipantAdmissionEligibilityRepository>());
        services.AddScoped<
            IAdmissionTicketTransferRepository,
            AdmissionTicketTransferRepository>();
        services.AddScoped<
            IFairReturnWaitlistRepository,
            FairReturnWaitlistRepository>();
        services.AddScoped<
            IFairReturnOrchestrationRepository,
            FairReturnOrchestrationRepository>();
        services.AddScoped<IAdmissionCheckInTransaction, AdmissionCheckInRepository>();
        services.AddScoped<AdmissionCheckInReportingRepository>();
        services.AddScoped<IAdmissionCheckInSummaryQuery>(provider =>
            provider.GetRequiredService<AdmissionCheckInReportingRepository>());
        services.AddScoped<IAdmissionCheckInReportingRepository>(provider =>
            provider.GetRequiredService<AdmissionCheckInReportingRepository>());
        services.AddScoped<AdmissionTargetOperationsRepository>();
        services.AddScoped<IAdmissionTargetOperationsRepository>(provider =>
            provider.GetRequiredService<AdmissionTargetOperationsRepository>());
        services.AddScoped<IAdmissionCheckInHealthProbe, AdmissionCheckInDatabaseHealthProbe>();
        services.AddScoped<AdmissionScannerCapabilityRepository>();
        services.AddScoped<IAdmissionScannerCapabilityRepository>(provider =>
            provider.GetRequiredService<AdmissionScannerCapabilityRepository>());
        services.AddScoped<IAnonymousCancellationRepository, AnonymousCancellationRepository>();
        services.AddScoped<AdmissionRevocationRepository>();
        services.AddScoped<IAdmissionRevocationRepository>(provider =>
            provider.GetRequiredService<AdmissionRevocationRepository>());
        services.AddScoped<IAdmissionEventCancellationRepository>(provider =>
            provider.GetRequiredService<AdmissionRevocationRepository>());
        services.AddScoped<IAdmissionRecoveryRepository, AdmissionRecoveryRepository>();
        services.AddScoped<IAdmissionRecoveryIdentityResolver, AdmissionRecoveryIdentityResolver>();
        services.AddScoped<AdmissionTicketRepository>();
        services.AddScoped<IAdmissionTicketRecoveryRepository>(provider =>
            provider.GetRequiredService<AdmissionTicketRepository>());
        services.AddScoped<IAdmissionTicketAccountRepository, AdmissionTicketAccountRepository>();
        services.AddScoped<IAdmissionTicketPresentationResolver, AdmissionTicketPresentationResolver>();
        services.AddScoped<AdmissionRecoveryProtectedDeliveryService>();
        services.AddScoped<IAdmissionRecoveryRequestStager, AdmissionRecoveryRequestStager>();
        services.AddScoped<IAdmissionRecoveryRequestOutboxHandler, AdmissionRecoveryRequestOutboxHandler>();
        services.AddScoped<IAdmissionRecoveryDeliveryStager>(provider =>
            provider.GetRequiredService<AdmissionRecoveryProtectedDeliveryService>());
        services.AddScoped<IAdmissionRecoveryDeliveryOutboxHandler, AdmissionRecoveryDeliveryOutboxHandler>();
        services.AddScoped<IAdmissionCredentialDeliveryOutboxHandler, AdmissionCredentialDeliveryOutboxHandler>();
        services.AddScoped<IAdmissionDeliveryDispatcher, AdmissionDeliveryIntentDispatcher>();
        services.AddScoped<IRegistrationProviderSubmissionWriteEffectRepository, RegistrationProviderSubmissionWriteEffectRepository>();
        services.AddScoped<
            ITicketPurchaseGovernanceRepository,
            TicketPurchaseGovernanceRepository>();
        services.AddScoped<IPaidEventPolicyRepository, PaidEventPolicyRepository>();
        services.AddScoped<IPaidCheckoutActivationRepository, PaidCheckoutActivationRepository>();
        services.AddScoped<IPlatformFeePolicyRepository, PlatformFeePolicyRepository>();
        services.AddScoped<IPlatformContributionSettingRepository, PlatformContributionSettingRepository>();
        services.AddScoped<IOrganizerPaymentProviderAccountOperationRepository, OrganizerPaymentProviderAccountOperationRepository>();
        services.AddScoped<IOrganizerPaymentProviderConnectionRepository, OrganizerPaymentProviderConnectionRepository>();
        services.AddScoped<IRegistrationPaymentAttemptRepository, RegistrationPaymentAttemptRepository>();
        services.AddScoped<IRefundAttemptRepository, RefundAttemptRepository>();
        services.AddScoped<IRefundCampaignRepository, RefundCampaignRepository>();
        services.AddScoped<IRegistrationMaterialChangeChoiceRepository, RegistrationMaterialChangeChoiceRepository>();
        services.AddScoped<IEventPublicActionRepository, EventPublicActionRepository>();
        services.AddScoped<IEventOrganizerClaimRepository, EventOrganizerClaimRepository>();
        services.AddScoped<IEventDiscoveryIdentityRepository, EventDiscoveryIdentityRepository>();
        services.AddScoped<IEventDiscoveryDisclosureRepository, EventDiscoveryDisclosureRepository>();
        services.AddScoped<IEventDiscoveryResponseBoundaryRepository, EventDiscoveryResponseBoundaryRepository>();
        services.AddScoped<IEventDiscoverySnapshotRepository, EventDiscoverySnapshotRepository>();
        services.AddScoped<IEventDiscoverySnapshotMaintenanceRepository, EventDiscoverySnapshotMaintenanceRepository>();
        services.AddScoped<IOrganizationTenantEvidenceRepository, OrganizationTenantEvidenceRepository>();
        services.AddScoped<IEventHeavyRedactionRepository, EventHeavyRedactionRepository>();
        services.AddScoped<IEventModerationRecordRepository, EventModerationRecordRepository>();
        services.AddScoped<IEventReportRepository, EventReportRepository>();
        services.AddScoped<IEventResourceRepository, EventResourceRepository>();
        services.AddScoped<IEventResourceAuditRetentionRepository, EventResourceAuditRetentionRepository>();
        services.AddScoped<IEventResourceProviderActivationRepository, EventResourceProviderActivationRepository>();
        services.AddScoped<IEventReportDecisionExecutionRepository, EventReportDecisionExecutionRepository>();
        services.AddScoped<IEventSessionRepository, EventSessionRepository>();
        services.AddScoped<IEventSessionGroupRepository, EventSessionGroupRepository>();
        services.AddScoped<IEventSessionGroupSessionRepository, EventSessionGroupSessionRepository>();
        services.AddScoped<IEventSessionIslamicAspectRepository, EventSessionIslamicAspectRepository>();
        services.AddScoped<IEventRegistrationRepository, EventRegistrationRepository>();
        services.AddScoped<IEventDayRepository, EventDayRepository>();
        services.AddScoped<IEventAgendaItemRepository, EventAgendaItemRepository>();
        services.AddScoped<IEventSessionAgendaItemRepository, EventSessionAgendaItemRepository>();
        services.AddScoped<IEventSessionLanguageRepository, EventSessionLanguageRepository>();
        services.AddScoped<IEventSessionSpeakerRepository, EventSessionSpeakerRepository>();

        // Event Custom Property Repositories
        services.AddScoped<IEventTemplateRepository, EventTemplateRepository>();
        services.AddScoped<IEventCustomPropertyRepository, EventCustomPropertyRepository>();

        // Event Session Custom Property Repositories
        services.AddScoped<IEventSessionTemplateRepository, EventSessionTemplateRepository>();
        services.AddScoped<IEventSessionCustomPropertyRepository, EventSessionCustomPropertyRepository>();

        // Custom Property Projection Coordination
        services.AddScoped<ICustomPropertyProjectionStatusRepository, CustomPropertyProjectionStatusRepository>();
        services.AddScoped<ICustomPropertyProjectionDirtyScopeRepository, CustomPropertyProjectionDirtyScopeRepository>();
        services.AddScoped<ICustomPropertyQuotaResolver, Services.CustomPropertyQuotaResolver>();
        services.AddScoped<IEventCustomPropertyProjectionUpdater, Projections.EventCustomPropertyProjectionUpdater>();
        services.AddScoped<IEventSessionCustomPropertyProjectionUpdater, Projections.EventSessionCustomPropertyProjectionUpdater>();

        // Custom Property Projection Query Repositories
        services.AddScoped<IEventCustomPropertyProjectionRepository, EventCustomPropertyProjectionRepository>();
        services.AddScoped<IEventSessionCustomPropertyProjectionRepository, EventSessionCustomPropertyProjectionRepository>();
        services.AddScoped<IEventAggregateViewRepository, EventAggregateViewRepository>();
        services.AddScoped<ICustomPropertyGovernanceRepository, CustomPropertyGovernanceRepository>();
        services.AddScoped<
            IConfigurationImportSessionRepository,
            ConfigurationImportSessionRepository>();
        services.AddScoped<
            IConfigurationImportArtifactStore,
            ConfigurationImportArtifactRepository>();
        services.AddScoped<
            IConfigurationImportOperationRepository,
            ConfigurationImportOperationRepository>();
        services.AddScoped<IConfigurationImportEffectOutboxRepository,
            OutboxRepository>();
        services.AddScoped<IConfigurationDirectTransferRepository,
            ConfigurationDirectTransferRepository>();
        services.AddScoped<IConfigurationManagedApplyScheduleRepository,
            ConfigurationManagedApplyScheduleRepository>();
        services.AddScoped<IConfigurationDirectTransferChunkStore,
            ConfigurationDirectTransferChunkRepository>();

        // Event Aspect Repositories
        services.AddScoped<IEventIslamicAspectRepository, EventIslamicAspectRepository>();
        services.AddScoped<IEventTechAspectRepository, EventTechAspectRepository>();
        services.AddScoped<IEventSeriesRepository, EventSeriesRepository>();
        services.AddScoped<IEventContactShareConsentRepository, EventContactShareConsentRepository>();
        services.AddScoped<IEventContactShareExportRepository, EventContactShareExportRepository>();

        // Location Repository
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ILocalAddressSuggestionQuery, LocalAddressSuggestionQuery>();
        services.AddScoped<ILocationRoomRepository, LocationRoomRepository>();
        services.AddScoped<IEventLocationRepository, EventLocationRepository>();
        services.AddScoped<IEventLocationDisclosureAuditRepository, EventLocationDisclosureAuditRepository>();
        services.AddScoped<IEventLocationExactReadAuditRepository, EventLocationExactReadAuditRepository>();
        services.AddScoped<IPrivacyErasureReplayCheckpointRepository, PrivacyErasureReplayCheckpointRepository>();
        services.AddScoped<IPrivacyErasureStateRepository, PrivacyErasureStateRepository>();
        services.AddScoped<IPrivacyErasureProviderWorkRepository, PrivacyErasureProviderWorkRepository>();
        services.AddScoped<IUserLocationPrivacyErasureRepository, UserLocationPrivacyErasureRepository>();
        services.AddScoped<IUserPrivacyErasureRepository, UserLocationPrivacyErasureRepository>();
        if (erasureDurability.Topology == PrivacyErasureAuthorityTopology.ExternalDatabase)
        {
            PrimaryDatabaseConnectionOptions authorityDatabaseOptions =
                PrivacyErasureAuthorityDatabaseConfiguration.BindRuntime(configuration);
            PrivacyErasureAuthorityDatabaseConfiguration.EnsureDistinctPhysicalDatabase(
                applicationRuntimeOptions,
                authorityDatabaseOptions);
            PrimaryDatabaseConnectionResult authorityDatabase =
                PrimaryDatabaseConfiguration.BuildConnectionString(authorityDatabaseOptions);

            services.AddDbContext<PrivacyErasureAuthorityDbContext>(options =>
                options.UseNpgsql(
                        authorityDatabase.ConnectionString,
                        npgsql => npgsql
                            .MigrationsAssembly(typeof(PrivacyErasureAuthorityDbContext).Assembly.FullName)
                            .MigrationsHistoryTable(
                                PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable))
                    .UseSnakeCaseNamingConvention());
            services.AddScoped<IPrivacyErasureAuthority, EfCorePrivacyErasureAuthorityRepository>();
        }
        else if (erasureDurability.Topology == PrivacyErasureAuthorityTopology.CoLocated)
        {
            if (string.IsNullOrWhiteSpace(applicationConnectionString))
            {
                throw new OptionsValidationException(
                    nameof(PrivacyErasureDurabilityOptions),
                    typeof(PrivacyErasureDurabilityOptions),
                    ["CoLocated requires a valid primary database runtime configuration."]);
            }

            if (applicationProvider == PrimaryDatabaseProvider.PostgreSql)
            {
                services.AddDbContext<CoLocatedPrivacyErasureAuthorityDbContext>(options =>
                    PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
                        options,
                        applicationRuntimeOptions));
                services.TryAddSingleton<TimeProvider>(TimeProvider.System);
                services.AddScoped<IPrivacyErasureAuthority,
                    CoLocatedPostgresPrivacyErasureAuthorityRepository>();
                services.AddScoped<IPrivacyErasureAuthorityMaintenance>(provider =>
                    (IPrivacyErasureAuthorityMaintenance)provider.GetRequiredService<IPrivacyErasureAuthority>());
            }
            else if (applicationProvider == PrimaryDatabaseProvider.Sqlite)
            {
                services.AddDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                    EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(
                        options,
                        applicationRuntimeOptions));
                services.TryAddSingleton<TimeProvider>(TimeProvider.System);
                services.AddScoped<IPrivacyErasureAuthority, EmbeddedPrivacyErasureAuthorityRepository>();
                services.AddScoped<IPrivacyErasureAuthorityMaintenance>(provider =>
                    (IPrivacyErasureAuthorityMaintenance)provider.GetRequiredService<IPrivacyErasureAuthority>());
            }
            else
            {
                throw new OptionsValidationException(
                    nameof(PrivacyErasureDurabilityOptions),
                    typeof(PrivacyErasureDurabilityOptions),
                    [PrimaryDatabaseProviderComposition.UnsupportedCoLocatedPrivacyErasureAuthorityMessage]);
            }
        }
        else
        {
            EmbeddedPrivacyErasureAuthorityOptions embedded =
                EmbeddedPrivacyErasureAuthorityOptions.Bind(configuration);
            if (applicationProvider == PrimaryDatabaseProvider.Sqlite)
            {
                EnsureDedicatedEmbeddedAuthorityFile(applicationConnectionString!, embedded.Path);
            }
            services.AddSingleton(embedded);
            services.AddSingleton<EmbeddedPrivacyErasureAuthorityStorage>();
            services.AddDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(options, embedded));
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<IPrivacyErasureAuthority>(provider =>
                new EmbeddedPrivacyErasureAuthorityRepository(
                    provider.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<IOptions<PrivacyErasureOptions>>(),
                    provider.GetRequiredService<EmbeddedPrivacyErasureAuthorityStorage>()));
            services.AddSingleton<IPrivacyErasureAuthorityMaintenance>(provider =>
                (IPrivacyErasureAuthorityMaintenance)provider.GetRequiredService<IPrivacyErasureAuthority>());
        }

        services.AddScoped<IPrivacyIdentityFenceAuthority>(provider =>
            (IPrivacyIdentityFenceAuthority)provider.GetRequiredService<IPrivacyErasureAuthority>());
        services.AddScoped<IPrivacyErasureRecoveryAuthority>(provider =>
            (IPrivacyErasureRecoveryAuthority)provider.GetRequiredService<IPrivacyErasureAuthority>());
        services.AddScoped<IPrivacyIdentityBindingReader, UserExternalLoginRepository>();

        // Storage Repository
        services.AddScoped<IStorageObjectRepository, StorageObjectRepository>();
        services.AddScoped<IStorageProducerOperationRepository>(provider =>
            (IStorageProducerOperationRepository)provider.GetRequiredService<IStorageObjectRepository>());
        services.AddScoped<IStorageProviderBindingRepository, StorageProviderBindingRepository>();
        services.AddScoped<IStorageObjectDeletionTombstoneRepository, StorageObjectDeletionTombstoneRepository>();
        services.AddScoped<IStorageObjectReferenceRepository, StorageObjectReferenceRepository>();
        services.AddScoped<IStorageObjectRetirementEligibilityReader, StorageObjectReferenceRepository>();
        services.AddScoped<IEventResourceStorageLifecycleRepository, EventResourceStorageLifecycleRepository>();
        services.AddScoped<IStorageUploadSessionRepository, StorageUploadSessionRepository>();
        services.AddScoped<IStorageUsageCounterRepository, StorageUsageCounterRepository>();

        // Tag & Category Repositories
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ITagTypeRepository, TagTypeRepository>();
        services.AddScoped<ITagTypeTagsRepository, TagTypeTagsRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryTypeRepository, CategoryTypeRepository>();
        services.AddScoped<ICategoryTypeCategoriesRepository, CategoryTypeCategoriesRepository>();
        services.AddScoped<IEventTagsRepository, EventTagsRepository>();
        services.AddScoped<IEventCategoriesRepository, EventCategoriesRepository>();

        // ATProto/Federation Repositories
        services.AddScoped<IAtprotoRecordRepository, AtprotoRecordRepository>();
        services.AddScoped<IAtprotoEventProjectionRepository, AtprotoEventProjectionRepository>();
        services.AddScoped<IAtprotoJetstreamRepository, AtprotoJetstreamRepository>();
        services.AddScoped<IAtprotoPdsSnapshotRepository, AtprotoJetstreamRepository>();
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();

        // Settings Repositories
        services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.AddScoped<ITenantSettingRepository, TenantSettingRepository>();
        services.AddScoped<ITenantSettingsDocumentRepository, TenantSettingsDocumentRepository>();
        services.AddScoped<IOrganizationSettingRepository, OrganizationSettingRepository>();
        services.AddScoped<IGroupSettingRepository, GroupSettingRepository>();
        services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
        services.AddScoped<INotificationChannelPreferenceRepository, NotificationChannelPreferenceRepository>();
        services.AddScoped<INotificationPreferenceProfileRepository, NotificationPreferenceProfileRepository>();
        services.AddScoped<IWebPushSubscriptionRepository, WebPushSubscriptionRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<ISecretBindingRepository, SecretBindingRepository>();
        services.AddScoped<IUiThemeRepository, UiThemeRepository>();
        services.AddScoped<IUiThemePresetRepository, UiThemePresetRepository>();
        services.AddScoped<IUserAppearanceProfileRepository, UserAppearanceProfileRepository>();
        services.AddScoped<IUserAppearancePreferenceRepository, UserAppearancePreferenceRepository>();

        // Module Governance Repositories
        services.AddScoped<IModuleDefinitionRepository, ModuleDefinitionRepository>();
        services.AddScoped<ITenantCapabilityRepository, TenantCapabilityRepository>();

        // PDS Synchronization Repositories
        services.AddScoped<IPdsSyncOutboxRepository, PdsSyncOutboxRepository>();

        // Generic Outbox Repositories
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IConfigurationManifestEffectOutboxRepository, OutboxRepository>();
        services.AddScoped<IEmailDispatchOutboxRepository, EmailDispatchOutboxRepository>();
        services.AddScoped<IEmailDispatchEligibilityEvaluator, EmailDispatchEligibilityEvaluator>();
        services.AddScoped<IEmailDeliveryDisableImpactReader, EmailDeliveryDisableImpactReader>();
        services.AddScoped<IEmailDeliverySettingsWriter, EmailDeliverySettingsWriter>();
        services.AddScoped<IVisitorAccessSettingsWriter, VisitorAccessSettingsWriter>();
        services.AddScoped<IEventResourceSettingsWriter, EventResourceSettingsWriter>();
        services.AddScoped<IWebPushDispatchOutboxRepository, WebPushDispatchOutboxRepository>();
        services.AddScoped<IIntegrationSyncOutboxRepository, IntegrationSyncOutboxRepository>();
        services.AddScoped<IQueueDrainHealthRepository, QueueDrainHealthRepository>();
        services.AddScoped<IRegistrationProviderSubmissionWriteEffectRepository, RegistrationProviderSubmissionWriteEffectRepository>();

        // Webhook Repositories
        services.AddScoped<IWebhookConsumerRepository, WebhookConsumerRepository>();
        services.AddScoped<IWebhookConsumerProviderBindingRepository, WebhookConsumerProviderBindingRepository>();
        services.AddScoped<IWebhookEventTypeRepository, WebhookEventTypeRepository>();
        services.AddScoped<IWebhookEndpointRepository, WebhookEndpointRepository>();
        services.AddScoped<IWebhookMessageRepository, WebhookMessageRepository>();
        services.AddScoped<IWebhookDeliveryAttemptRepository, WebhookDeliveryAttemptRepository>();
        services.AddScoped<IWebhookLocalTargetRepository, WebhookLocalTargetRepository>();
        services.AddScoped<IWebhookBulkReplayRepository, WebhookBulkReplayRepository>();
        services.AddScoped<IWebhookProviderPublicationRepository, WebhookProviderPublicationRepository>();
        services.AddScoped<IWebhookDeliveryPlanMaterializer, WebhookDeliveryPlanMaterializer>();
        services.AddScoped<IIncomingWebhookMessageRepository, IncomingWebhookMessageRepository>();
        services.AddScoped<IIncomingWebhookEffectOutboxRepository, IncomingWebhookEffectOutboxRepository>();
        services.AddScoped<IIncomingWebhookEffectReceiptRepository, IncomingWebhookEffectReceiptRepository>();
        services.AddScoped<IWebhookRetentionCleanupRepository, WebhookRetentionCleanupRepository>();

        // Authorization (RBAC) Repositories
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IEventRoleAssignmentRepository, EventRoleAssignmentRepository>();
        services.AddScoped<IEventAuthoritySnapshotService, Services.EventAuthoritySnapshotService>();

        // Configuration Audit Repositories
        services.AddScoped<IConfigurationChangeLogRepository, ConfigurationChangeLogRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IWebhookAuditEventRepository, WebhookAuditEventRepository>();
        services.AddScoped<ISupportAccessSessionRepository, SupportAccessSessionRepository>();
        services.AddScoped<ISupportAccessAuditEventRepository, SupportAccessAuditEventRepository>();

        // Governance Policy Resolver (deterministic hierarchy walk: Instance → Tenant → Organization)
        services.AddScoped<IPolicyResolver, Services.PolicyResolver>();
        services.AddScoped<INotificationPreferenceResolver, Services.NotificationPreferenceResolver>();

        // Notification Repository
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<NotificationIntentRepository>();
        services.AddScoped<INotificationIntentRepository>(provider => provider.GetRequiredService<NotificationIntentRepository>());
        services.AddScoped<IRecipientNotificationGraphRepository>(provider => provider.GetRequiredService<NotificationIntentRepository>());
        services.AddScoped<IModerationNoticeNotificationCleanup>(provider => provider.GetRequiredService<NotificationIntentRepository>());

        // Idempotency Repository
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IAtprotoBootstrapReplayRepository, AtprotoBootstrapReplayRepository>();
        services.AddScoped<IAtprotoTransientStoreRepository, AtprotoTransientStoreRepository>();
        services.AddScoped<IAtprotoTransientAssertionReplayRepository, AtprotoTransientAssertionReplayRepository>();

        // AI Assistant Repositories
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IAiConsentGrantRepository, AiConsentGrantRepository>();

        return services;
    }

    private static void EnsureDedicatedEmbeddedAuthorityFile(
        string? applicationConnectionString,
        string authorityPath)
    {
        if (string.IsNullOrWhiteSpace(applicationConnectionString))
        {
            return;
        }

        var application = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
            applicationConnectionString);
        if (!string.IsNullOrWhiteSpace(application.DataSource)
            && Path.GetFullPath(application.DataSource)
                .Equals(Path.GetFullPath(authorityPath), StringComparison.Ordinal))
        {
            throw new OptionsValidationException(
                nameof(PrivacyErasureDurabilityOptions),
                typeof(PrivacyErasureDurabilityOptions),
                ["EmbeddedSqlite requires a dedicated file distinct from the application database."]);
        }
    }

    private static bool IsEnabled(string? value)
    {
        return bool.TryParse(value, out var enabled) && enabled;
    }
}
