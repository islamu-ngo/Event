using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Recovery;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Contracts.SetupLive;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Domain;
using Explore.Persistence.Caching;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Queries;
using Explore.Persistence.Schema;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
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
    /// <summary>Registers the original scoped policy and manifest owners with their execution lease.</summary>
    public static IServiceCollection AddPolicyMutationExecutionScopes(this IServiceCollection services)
    {
        services.AddScoped<ExploreDbContextExecutionLease>();
        services.AddScoped<IPolicyMutationExecutionScopeFactory, PolicyMutationExecutionScopeFactory>();
        services.AddScoped<IHumanOriginalCredentialCustody>(provider =>
            (IHumanOriginalCredentialCustody)provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>());
        services.AddScoped<IHumanTenantPreferenceStore, HumanTenantPreferenceStore>();
        services.AddScoped<IConfigurationManifestTenantReadScopeFactory, ConfigurationManifestTenantReadScopeFactory>();
        services.AddScoped<IConfigurationInstanceImportExecutor, ConfigurationInstanceImportExecutor>();
        return services;
    }

    /// <summary>Registers one Global pool and its existing closed discovery/admission aliases.</summary>
    public static IServiceCollection AddGlobalExploreDbContextFactory(
        this IServiceCollection services, PrimaryDatabaseConnectionOptions options)
    {
        services.AddSingleton(new GlobalExploreDbContextFactory(options));
        services.AddSingleton<IGlobalExploreDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IPdsSettlementDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IPdsSyncQueueAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IEmailDispatchAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IAtprotoMaterializationDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<INotificationFanoutAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<INotificationOutboxAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IGenericOutboxAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IWebPushDispatchAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IWebhookLocalDeliveryAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        services.AddSingleton<IPaymentReconciliationAdmissionDbContextFactory>(provider =>
            provider.GetRequiredService<GlobalExploreDbContextFactory>());
        return services;
    }

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
        bool configureDatabaseComposition = !skipDbContextRegistration
            || erasureDurability.Topology == PrivacyErasureAuthorityTopology.CoLocated
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase)
                && configuration.GetValue<bool>("Testing:UseRealDatabase");

        string? applicationConnectionString = null;
        PrimaryDatabaseProvider? applicationProvider = null;
        PrimaryDatabaseConnectionOptions applicationRuntimeOptions = new()
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql
        };

        // Real-database test profiles need the purpose-owned factory graph even when
        // the fixture replaces its request DbContext or selects an embedded privacy store.
        if (configureDatabaseComposition)
        {
            var runtimeDatabaseOptions = PrimaryDatabaseConfiguration.BindRuntime(configuration);
            var runtimeDatabase = PrimaryDatabaseConfiguration.BuildConnectionString(runtimeDatabaseOptions);
            applicationConnectionString = runtimeDatabase.ConnectionString;
            applicationProvider = runtimeDatabase.Provider;
            applicationRuntimeOptions = runtimeDatabaseOptions;

            var globalDatabaseOptions = PrimaryDatabaseConfiguration.BindGlobal(configuration);
            if (runtimeDatabaseOptions.Provider == PrimaryDatabaseProvider.PostgreSql)
                services.AddSingleton(PostgresNativeQueueEnrollment.Bind(configuration));
            if (globalDatabaseOptions.Provider != PrimaryDatabaseProvider.Sqlite
                && string.Equals(globalDatabaseOptions.Username, runtimeDatabaseOptions.Username, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Global and Runtime database logins must remain separate.");
            }
            services.AddGlobalExploreDbContextFactory(globalDatabaseOptions);
            services.AddSingleton(new InstanceApiKeyDbContextFactories(
                runtimeDatabaseOptions, globalDatabaseOptions,
                [
                    PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.InstanceKeyRead),
                    PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.InstanceKeyWrite),
                    PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.InstanceKeyIssuance),
                    PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.PlatformKeyReport)
                ],
                runtimeDatabaseOptions.Provider == PrimaryDatabaseProvider.PostgreSql
                    ? PlatformApiKeySourceEnrollment.Bind(configuration)
                    : null));
            if (!skipDbContextRegistration)
                services.AddHostedService<InstanceApiKeyCapabilityAdmissionService>();
            services.AddSingleton<ITenantExploreDbContextFactory>(
                new TenantExploreDbContextFactory(runtimeDatabaseOptions));

            services.AddDbContext<DataProtectionKeyContext>(options =>
                PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, runtimeDatabaseOptions));

            // Use pooled DbContext factory for performance (EF Core recommended pattern)
            // The scoped ExploreDbContext registration below handles scoped dependency injection
            services.AddPooledDbContextFactory<ExploreDbContext>(options =>
            {
                PrimaryDatabaseProviderComposition.ConfigureApplication(options, runtimeDatabaseOptions);

                if (runtimeDatabase.Provider == PrimaryDatabaseProvider.PostgreSql)
                {
                    options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
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

            services.AddPolicyMutationExecutionScopes();
            services.AddCheckoutDispatchPersistence();

        }

        LocalIdentityOptions localIdentityOptions =
            configuration.GetSection(LocalIdentityOptions.SectionName).Get<LocalIdentityOptions>()
            ?? new LocalIdentityOptions();
        string? primaryProvider = configuration.GetSection(AuthenticationProviderDeploymentOptions.SectionName)
            .Get<AuthenticationProviderDeploymentOptions>()?.GetProvider();
        IdentityBuilder identityBuilder = services.AddIdentityCore<LocalIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = localIdentityOptions.LockoutThreshold;
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(localIdentityOptions.LockoutDurationMinutes);
                options.Password.RequiredLength = 1;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
            })
            .AddRoles<LocalIdentityRole>()
            .AddUserValidator<OptionalEmailLocalIdentityUserValidator>();
        services.AddLocalPasswordPolicy(localIdentityOptions,
            required: primaryProvider is null or AuthenticationProviderDeploymentOptions.LocalProvider);

        IdentityDatabaseTopology identityTopology = IdentityDatabaseConfiguration.GetTopology(configuration);
        services.TryAddScoped<LocalIdentityExecutionLease>();
        services.TryAddScoped<ILocalIdentityExecutionScopeFactory, LocalIdentityExecutionScopeFactory>();
        services.AddSingleton(_ =>
        {
            var applicationWriter = PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.LocalIdentityWriter);
            var externalWriter = identityTopology == IdentityDatabaseTopology.External
                ? IdentityDatabaseConfiguration.BindExternal(configuration, PrimaryDatabaseRole.LocalIdentityWriter) : null;
            PostgresLocalIdentityWriterEnrollment? applicationEnrollment = applicationWriter.Provider == PrimaryDatabaseProvider.PostgreSql
                ? new(PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Migrator),
                    PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Runtime), applicationWriter.Username!,
                    PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Global)) : null;
            PostgresLocalIdentityWriterEnrollment? identityEnrollment = externalWriter?.Provider == PrimaryDatabaseProvider.PostgreSql
                ? new(configuration["IdentityDatabase:Migrator:Username"] ?? configuration["IDENTITY_DATABASE_MIGRATOR_USERNAME"] ?? string.Empty,
                    configuration["IdentityDatabase:Runtime:Username"] ?? configuration["IDENTITY_DATABASE_RUNTIME_USERNAME"] ?? string.Empty,
                    new Npgsql.NpgsqlConnectionStringBuilder(externalWriter.ConnectionString).Username!) : null;
            return new LocalIdentityWriterDbContextFactory(applicationWriter, externalWriter,
                applicationEnrollment: applicationEnrollment, identityEnrollment: identityEnrollment);
        });
        services.AddScoped<ILocalBootstrapReceiptSource>(provider =>
            new ConfiguredLocalBootstrapReceiptSource(provider.GetRequiredService<ExploreDbContext>(),
                () => provider.GetRequiredService<LocalIdentityWriterDbContextFactory>()));
        services.AddScoped<IHumanTenantCreationGraphWriter>(provider => new HumanTenantCreationGraphWriter(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<ExploreDbContextExecutionLease>(),
            provider.GetRequiredService<IHumanTenantCreationPort>(),
            identityTopology));
        services.AddScoped<ManagedTenantWorkerCreationAdmission>();
        services.AddScoped<IManagedTenantWorkerCreationAdmission>(provider =>
            provider.GetRequiredService<ManagedTenantWorkerCreationAdmission>());
        services.AddScoped<IManagedTenantWorkerCreationOwnerFactory, ManagedTenantWorkerCreationOwnerFactory>();
        services.AddScoped<IManagedTenantLocalEligibilitySource>(provider =>
            new ConfiguredManagedTenantLocalEligibilitySource(provider.GetRequiredService<ExploreDbContext>(),
                identityTopology, identityTopology == IdentityDatabaseTopology.External
                    ? provider.GetRequiredService<LocalIdentityWriterDbContextFactory>() : null,
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new ManagedTenantWorkerCreationGraphWriter(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<ExploreDbContextExecutionLease>(),
            provider.GetRequiredService<IManagedTenantLocalEligibilitySource>(),
            provider.GetRequiredService<Explore.Application.Services.PrivacyIdentityFenceOperation>(),
            provider.GetRequiredService<IPrivacyIdentityFenceAuthority>(), provider.GetRequiredService<IOriginalOwnerSettlementBoundary>(),
            provider.GetRequiredService<IEventDiscoveryEnrollmentGate>(), provider.GetRequiredService<ISettingMutationLock>(),
            provider.GetRequiredService<IOptions<Explore.Application.Management.ManagedControlPlaneOptions>>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<PostgresNativeQueueEnrollment>(), identityTopology));
        services.AddScoped<Explore.Application.Features.Management.ManagedTenantProvisioningSchedulingOperation>();
        services.AddScoped<IManagedTenantProvisioningSchedulingSource, ManagedTenantProvisioningSchedulingSource>();
        services.AddScoped<IManagedTenantProvisioningSchedulingPort>(provider => new ManagedTenantProvisioningSchedulingPort(
            provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
            provider.GetService<PostgresNativeQueueEnrollment>(), identityTopology));
        services.AddHumanEventAssignmentSources(identityTopology);
        services.AddEventTeamPolicySources();
        services.AddHumanSupportAccessSources(identityTopology);
        services.AddHumanSupportAdministrationSources();
        services.AddHumanWebhookResourceSources(identityTopology);
        services.AddHumanEventProviderSources(identityTopology);
        services.AddHumanEmailSources(identityTopology);
        services.AddHumanMixedSettingsSources(identityTopology);
        services.AddPlatformApiKeySources(identityTopology);
        Explore.Persistence.Security.StorageAccountingOriginalOwnerRegistration
            .AddStorageAccountingOriginalOwner(services);
        Explore.Persistence.Security.StorageProbeSourceOwnerRegistration
            .AddStorageProbeSourceOwner(services);
        services.AddFooterSettingsWriter();
        services.AddScoped<Explore.Application.Features.InstanceOnboarding.Services.ITrustedSetupSettingsWriter,
            TrustedSetupSettingsWriter>();
        services.AddScoped<Explore.Application.Features.InstanceOnboarding.Services.IProviderConfigurationSettingsWriter,
            ProviderConfigurationSettingsWriter>();
        services.AddScoped<Explore.Application.Settings.IOperatorIdentitySettingsWriter,
            OperatorIdentitySettingsWriter>();
        services.AddScoped<Explore.Application.Settings.IResolverConfigurationSettingsWriter,
            ResolverConfigurationSettingsWriter>();
        services.AddStorageConfigurationWriter();
        services.AddScoped<IRegistrationProviderDeferredCheckpointJournal>(provider =>
            new RegistrationProviderDeferredCheckpointJournal(
                () => provider.GetRequiredService<ExploreDbContext>(),
                () => provider.GetRequiredService<IHumanEventProviderMutationPreparation>()));
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
        if (configureDatabaseComposition)
            services.AddLocalIdentityExecutionContextSelection(identityTopology);
        identityBuilder.AddDefaultTokenProviders().AddLocalLifecycleTokenProviders();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped(serviceProvider =>
        {
            DbContext identityDbContext = identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>();
            var application = serviceProvider.GetRequiredService<ExploreDbContext>();
            return new LocalIdentityCredentialStateStore(
                identityDbContext: identityDbContext,
                applicationDbContext: application,
                userManager: serviceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
                timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
                executionLease: GetSelectedLocalIdentityExecution(serviceProvider, application));
        });
        services.AddScoped<ILocalIdentityLifecycleStore>(serviceProvider => new LocalIdentityLifecycleStore(
            identityDbContext: identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>(),
            applicationDbContext: serviceProvider.GetRequiredService<ExploreDbContext>(),
            userManager: serviceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
            timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
            credentialStates: serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>()));
        services.AddScoped<ILocalIdentityLifecycleDeliveryStore>(serviceProvider => new LocalIdentityLifecycleDeliveryStore(
            identityDbContext: identityTopology == IdentityDatabaseTopology.External
                ? serviceProvider.GetRequiredService<ExternalIdentityDbContext>()
                : serviceProvider.GetRequiredService<ExploreDbContext>(),
            applicationDbContext: serviceProvider.GetRequiredService<ExploreDbContext>(),
            credentialStates: serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>(),
            timeProvider: serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ILocalIdentityAuthService, LocalIdentityAuthService>();
        services.AddScoped<ILocalCredentialAdministration>(serviceProvider =>
            serviceProvider.GetRequiredService<LocalIdentityCredentialStateStore>());
        Explore.Persistence.Identity.LocalAdministrationSourceRegistration
            .AddLocalAdministrationSources(services);

        // Unit of Work (wraps EF Core transactions)
        Security.HumanManagedProviderProvisioningDataRegistration.AddHumanManagedProviderProvisioningData(services);
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<IOriginalOwnerSettlementBoundary>(provider =>
            (IOriginalOwnerSettlementBoundary)provider.GetRequiredService<IUnitOfWork>());
        services.AddScoped<ISerializableAttemptOwner, EfCoreUnitOfWork>();
        services.AddScoped<Explore.Application.Contracts.Services.Registration.IAnonymousRegistrationChallengeQuota,
            AnonymousRegistrationChallengeQuota>();
        services.AddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.AddScoped<IAtprotoSessionRefreshLock, RelationalAtprotoSessionRefreshLock>();
        services.AddScoped<ISetupLiveRepository, SetupLiveRepository>();
        services.AddScoped<IKeycloakOperationRepository, KeycloakOperationRepository>();
        services.AddScoped<IAuthenticationCredentialCutoverRepository, AuthenticationCredentialCutoverRepository>();
        services.AddScoped<IAuthenticationCredentialCutoverCoordinator, RelationalAuthenticationCredentialCutoverCoordinator>();
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
        services.AddScoped<PublicEventDiscoveryReadBindingFactory>(provider => new(provider));
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantLookupSource, TenantLookupSource>();
        services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
        services.AddScoped<IInstanceBootstrapStateReader, InstanceBootstrapStateReader>();
        services.AddScoped<IIdentitySynchronizationBootstrapReader, IdentitySynchronizationBootstrapReader>();
        services.AddScoped<IPlatformUserRoleRepository, PlatformUserRoleRepository>();
        services.AddScoped<ITenantUserRepository, TenantUserRepository>();
        services.AddScoped<IUserTenantMembershipRepository>(provider => new UserTenantMembershipRepository(
            provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
            provider.GetRequiredService<ITenantExploreDbContextFactory>(),
            provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IUserTenantNavigationRepository>(provider => new UserTenantNavigationRepository(
            provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
            provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IAdministrativeAuthorityRepository, AdministrativeAuthorityRepository>();
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
        services.AddScoped<IInstanceApiKeyExecutionScopeFactory, InstanceApiKeyExecutionScopeFactory>();
        services.AddScoped<Explore.Application.Features.ExternalApiKeys.InstanceApiKeyOperations>();
        services.AddScoped<IExternalApiKeyAuthenticationRepository, ExternalApiKeyAuthenticationRepository>();
        services.AddScoped<IExternalApiKeyIssuanceReceiptRepository, ExternalApiKeyIssuanceReceiptRepository>();
        services.AddScoped<IExternalApiKeyIssuanceAuthority>(provider => new ExternalApiKeyIssuanceAuthority(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()));
        services.AddScoped<Explore.Application.Contracts.Identity.IMachineMutationCommitAuthority, MachineMutationCommitAuthority>();
        services.AddScoped<IInstanceAdministratorCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<ITenantAdministratorCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<IHumanSubjectCommitAuthority>(HumanAdministratorAuthority);
        services.AddScoped<IPolicySourceSetFence>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary policy source sets.");
            if (GetSelectedLocalIdentityExecution(provider, context) is { } selected)
                return new PolicySourceSetFence(selected);
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new PolicySourceSetFence(context, purpose, context.Database.IsNpgsql()
                ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null);
        });
        services.AddScoped<IHumanPrincipalSourceFence>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Principal sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException("Principal sources require validated credential custody.");
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary principal sources.");
            if (GetSelectedLocalIdentityExecution(provider, context) is { } selected)
                return new HumanPrincipalSourceFence(selected, subject, lifetime.LocalSession);
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            DbContext? identityContext = lifetime.LocalSession is null ? null
                : identityTopology == IdentityDatabaseTopology.External
                    ? provider.GetRequiredService<ExternalIdentityDbContext>() : context;
            return new HumanPrincipalSourceFence(context, subject, purpose, lifetime.LocalSession, identityContext,
                context.Database.IsNpgsql()
                    ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null);
        });
        services.AddScoped<IHumanAuthorizationRouteSource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException("Route sources require the original resolved human.");
            if (lease.HumanLifetime is null)
                throw new UnauthorizedAccessException("Route sources require validated credential custody.");
            if (lease.PolicyPurpose is PolicyMutationPurpose.EmailDelivery or PolicyMutationPurpose.MixedSettings)
                throw new InvalidOperationException("Email-only owners cannot acquire ordinary route sources.");
            if (GetSelectedLocalIdentityExecution(provider, context) is { } selected)
                return new HumanAuthorizationRouteSource(selected, subject);
            var purpose = context.IsConfigurationInstanceImportOwner
                ? PolicySourceSetPurpose.ConfigurationImport
                : lease.SelectedTenantId.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
            return new HumanAuthorizationRouteSource(context, subject, purpose,
                context.Database.IsNpgsql()
                    ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null);
        });
        services.AddScoped<IHumanActionCommitFinalizer, HumanActionCommitFinalizer>();
        services.AddScoped<IAccountMembershipPolicySource>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(context);
            if (lease.PolicyPurpose is not null
                || context.IsConfigurationInstanceImportOwner
                || !lease.SelectedTenantId.HasValue)
                throw new InvalidOperationException(
                    "Account membership requires its ordinary Runtime owner.");
            var subject = lease.HumanScope?.Subject
                ?? throw new UnauthorizedAccessException(
                    "Account sources require the original resolved human.");
            var lifetime = lease.HumanLifetime
                ?? throw new UnauthorizedAccessException(
                    "Account sources require original authenticated lifetime.");
            return new AccountMembershipPolicySource(context, subject, lifetime,
                context.Database.IsNpgsql()
                    ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>())
                    : null);
        });
        static ExternalApiKeyIssuanceAuthority HumanAdministratorAuthority(IServiceProvider provider)
        {
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            var scope = lease.HumanScope
                ?? throw new InvalidOperationException("Human authority requires an authenticated selected owner before construction.");
            return new ExternalApiKeyIssuanceAuthority(provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), scope);
        }
        services.AddScoped<IManagedControlPlaneRegistrationRepository, ManagedControlPlaneRegistrationRepository>();
        services.AddScoped<IManagedTenantProvisioningOperationRepository, ManagedTenantProvisioningOperationRepository>();
        services.AddScoped<IConfigurationManifestOperationRepository, ConfigurationManifestOperationRepository>();
        services.AddScoped<IConfigurationManifestFailureRecorder, ConfigurationManifestFailureRepository>();
        services.AddScoped<ILegalDocumentRepository, LegalDocumentRepository>();
        services.AddScoped<IConfigurationImportLegalSourceReader, ConfigurationImportLegalSourceReader>();
        services.AddScoped<IExternalApiKeyQuotaRepository, ExternalApiKeyQuotaRepository>();
        services.AddScoped<IExternalApiKeyTenantUsageReader, TenantApiKeyUsageReader>();
        services.AddScoped<IUserNotificationPreferenceRepository, UserNotificationPreferenceRepository>();
        services.AddScoped<IUserAuthenticationTokenRepository, UserAuthenticationTokenRepository>();
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddScoped<IExternalLoginAuthenticationRepository>(provider =>
            GetSelectedLocalIdentityExecution(provider, provider.GetRequiredService<ExploreDbContext>()) is { } selected
                ? new IdentityWriterExternalLoginAuthenticationRepository(selected)
                : new ExternalLoginAuthenticationRepository(
                provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IUserIdentityEmailRepository, UserIdentityEmailRepository>();
        services.AddScoped<IExternalBindingRepository, ExternalBindingRepository>();

        // Actor Repositories
        services.AddScoped<IActorRepository, ActorRepository>();
        services.AddScoped<IActorOwnershipReader>(provider => new ActorOwnershipReader(
            provider.GetRequiredService<ExploreDbContext>(),
            provider.GetService<PostgresNativeQueueEnrollment>()));
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
        services.AddRegistrationProviderSubscriptionWorker();
        services.AddScoped<IParticipationRequirementAttachmentRepository, ParticipationRequirementAttachmentRepository>();
        services.AddScoped<IEventTicketCatalogRepository, EventTicketCatalogRepository>();
        services.AddScoped<IRegistrationInventoryRepository, RegistrationInventoryRepository>();
        services.AddInventoryHoldExpiryWorker();
        services.AddScoped<IRegistrationOrderCreationReceiptRepository, RegistrationOrderCreationReceiptRepository>();
        services.AddScoped<IGuestRegistrationCapabilityRepository>(provider =>
            new GuestRegistrationCapabilityRepository(
                provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<IConfiguration>(),
                () => provider.GetRequiredService<ISerializableAttemptOwner>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IEventAddOnRepository, EventAddOnRepository>();
        services.AddScoped<TicketingRecoveryRepository>();
        services.AddTicketingRecoveryHealthPersistence();
        services.AddScoped<
            ITicketingRecoveryOperatorStore,
            TicketingRecoveryRepository>();
        services.AddScoped<IPromotionManagementRepository, PromotionManagementRepository>();
        services.AddScoped<IPromotionRedemptionRepository, PromotionRedemptionRepository>();
        services.AddScoped<IRegistrationParticipantRepository, RegistrationParticipantRepository>();
        services.AddScoped<IRegistrationSubmissionRepository, RegistrationSubmissionRepository>();
        services.AddScoped<IRegistrationProviderSubmissionEffectRepository, RegistrationProviderSubmissionEffectRepository>();
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
        services.AddScoped<IAdmissionTargetTransaction, AdmissionTargetTransaction>();
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
        services.AddScoped<IAdmissionScannerAuthenticationRepository, AdmissionScannerAuthenticationRepository>();
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
        services.AddScoped<IRegistrationProviderSubmissionWriteQueueRepository, RegistrationProviderSubmissionWriteQueueRepository>();
        services.AddScoped<IRegistrationProviderSubmissionWriteEffectRepository, RegistrationProviderSubmissionWriteEffectRepository>();
        services.AddScoped<IRegistrationProviderSubmissionWriteExecutionScopeFactory, RegistrationProviderSubmissionWriteExecutionScopeFactory>();
        services.AddScoped<Explore.Application.Services.Registration.Commands.RegistrationProviderSubmissionWriteEffectDeliveryService>();
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
        services.AddRefundReconciliationHealthPersistence();
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
        services.AddScoped<IEventReportOperationsRepository, EventReportOperationsRepository>();
        services.AddScoped<IEventResourceRepository, EventResourceRepository>();
        services.AddScoped<IEventResourceAuditOwnerDiscoveryRepository>(provider =>
            new EventResourceAuditOwnerDiscoveryRepository(
                provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IRegistrationRetentionTenantDiscoveryRepository>(provider =>
            new RegistrationRetentionTenantDiscoveryRepository(
                provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IRegistrationRetentionExecutionScopeFactory, RegistrationRetentionExecutionScopeFactory>();
        services.AddScoped<IEmailRetentionTenantDiscoveryRepository>(provider =>
            new EmailRetentionTenantDiscoveryRepository(
                provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IEmailRetentionExecutionScopeFactory, EmailRetentionExecutionScopeFactory>();
        services.AddScoped<IEventResourceAuditRetentionExecutionScopeFactory, EventResourceAuditRetentionExecutionScopeFactory>();
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
        services.AddScoped<IEventLocationPrivacyReviewReader, EventLocationPrivacyReviewReader>();
        services.AddScoped<ILocationPrivacyGovernanceStore, LocationPrivacyGovernanceStore>();
        services.AddScoped<ILocationPrivacyCorrectionReceiptRepository, LocationPrivacyCorrectionReceiptRepository>();
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
        services.AddScoped<ILocalIdentityEligibilityRevocation>(provider =>
            new ConfiguredLocalIdentityEligibilityRevocation(provider.GetRequiredService<ExploreDbContext>(),
                identityTopology, identityTopology == IdentityDatabaseTopology.External
                    ? provider.GetRequiredService<LocalIdentityWriterDbContextFactory>() : null,
                provider.GetRequiredService<IPrivacyErasureAuthority>(), provider.GetRequiredService<TimeProvider>()));
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
        services.AddScoped<IInstanceStorageAdministration, InstanceStorageAdministration>();
        services.AddScoped<StorageReconciliationOwner>();
        services.AddScoped<IStorageReconciliationOwner>(provider =>
            provider.GetRequiredService<StorageReconciliationOwner>());
        services.AddScoped<ILocalStorageQuarantineOwner>(provider =>
            provider.GetRequiredService<StorageReconciliationOwner>());

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
        services.AddScoped<IAtprotoFederationSettingRepository>(provider =>
            new AtprotoFederationSettingRepository(
                provider.GetRequiredService<ExploreDbContext>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IAtprotoFederationGlobalReadRepository>(provider =>
            new AtprotoFederationGlobalReadRepository(
                provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IInstanceTenantSettingsReader, InstanceTenantSettingsReader>();
        services.AddScoped<HumanSettingsDataBoundary>();
        services.AddScoped<ICoordinatedSettingMutationStore, CoordinatedSettingMutationRepository>();
        services.AddScoped<IOrdinarySettingsMutationStore, OrdinarySettingsMutationStore>();
        services.AddScoped<IHumanSecuritySourceGate>(provider =>
            new HumanSecuritySourceGate(provider.GetRequiredService<ExploreDbContext>()));
        services.AddScoped<Func<IHumanSecuritySourceGate>>(provider =>
            () => provider.GetRequiredService<IHumanSecuritySourceGate>());
        services.AddScoped<IEventDiscoveryEnrollmentGate, EventDiscoveryEnrollmentGate>();
        services.AddScoped<Func<IEventDiscoveryEnrollmentGate>>(provider =>
            () => provider.GetRequiredService<IEventDiscoveryEnrollmentGate>());
        services.AddScoped<AtprotoFederationSettingsMutationStore>();
        services.AddScoped<IAtprotoFederationSettingsMutationStore>(provider =>
            provider.GetRequiredService<AtprotoFederationSettingsMutationStore>());
        services.AddScoped<IAtprotoFederationSettingsWriter, AtprotoFederationSettingsWriter>();
        services.AddScoped<ITenantSettingRepository, TenantSettingRepository>();
        services.AddScoped<IManagedTenantDomainRepository, ManagedTenantDomainRepository>();
        services.AddScoped<ITenantSettingsDocumentRepository, TenantSettingsDocumentRepository>();
        services.AddScoped<IOrganizationSettingRepository, OrganizationSettingRepository>();
        services.AddScoped<IGroupSettingRepository, GroupSettingRepository>();
        services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
        services.AddScoped<INotificationChannelPreferenceRepository, NotificationChannelPreferenceRepository>();
        services.AddScoped<INotificationPreferenceProfileRepository, NotificationPreferenceProfileRepository>();
        services.AddScoped<IWebPushSubscriptionRepository, WebPushSubscriptionRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<ISecretBindingRepository, SecretBindingRepository>();
        services.AddScoped<ILocalizationSecretStatusReader, LocalizationSecretStatusReader>();
        services.AddScoped<IUiThemeRepository, UiThemeRepository>();
        services.AddScoped<IUiThemePresetRepository, UiThemePresetRepository>();
        services.AddScoped<IUserAppearanceProfileRepository, UserAppearanceProfileRepository>();
        services.AddScoped<IUserAppearancePreferenceRepository, UserAppearancePreferenceRepository>();

        // Module Governance Repositories
        services.AddScoped<IModuleDefinitionRepository, ModuleDefinitionRepository>();
        services.AddScoped<ITenantCapabilityRepository, TenantCapabilityRepository>();

        // PDS Synchronization Repositories
        services.AddScoped<IPdsSyncOutboxRepository, PdsSyncOutboxRepository>();
        services.AddScoped<IPdsSyncQueueAdmission, PdsSyncQueueAdmission>();
        services.AddScoped<IAtprotoModerationCompensationStore>(provider =>
            new AtprotoModerationCompensationStore(
                provider.GetRequiredService<ExploreDbContext>(),
                provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IAtprotoSessionBindingReader, AtprotoSessionBindingReader>();

        // Generic Outbox Repositories
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IConfigurationManifestEffectOutboxRepository, OutboxRepository>();
        services.AddScoped<IEmailDispatchOutboxRepository, EmailDispatchOutboxRepository>();
        services.AddScoped<IEmailDispatchAdmission, EmailDispatchAdmission>();
        services.AddScoped<IEmailDispatchHealthSnapshotReader, EmailDispatchHealthSnapshotReader>();
        services.AddScoped<INotificationFanoutAdmission, NotificationFanoutAdmission>();
        services.AddScoped<INotificationOutboxAdmission, NotificationOutboxAdmission>();
        services.AddScoped<INotificationOutboxExecutionAuthority, NotificationOutboxExecutionAuthority>();
        services.AddScoped<IGenericOutboxAdmission, GenericOutboxAdmission>();
        services.AddScoped<IGenericOutboxExecutionAuthority, GenericOutboxExecutionAuthority>();
        services.AddScoped<IGenericOutboxExecutionScopeFactory, GenericOutboxExecutionScopeFactory>();
        services.AddSingleton<IWebPushDispatchAdmission, WebPushDispatchAdmission>();
        services.AddSingleton<IWebPushDispatchExecutionScopeFactory, WebPushDispatchExecutionScopeFactory>();
        services.AddScoped<IIncomingWebhookQueueRepository, IncomingWebhookQueueRepository>();
        services.AddScoped<IRegistrationFinalizationQueueRepository, RegistrationFinalizationQueueRepository>();
        services.AddScoped<IPaymentReconciliationAdmission, PaymentReconciliationAdmission>();
        services.AddScoped<IPaymentReconciliationExecutionScopeFactory, PaymentReconciliationExecutionScopeFactory>();
        services.AddSingleton<IWebhookLocalDeliveryAdmission, WebhookLocalDeliveryAdmission>();
        services.AddSingleton<IWebhookLocalDeliveryExecutionScopeFactory, WebhookLocalDeliveryExecutionScopeFactory>();
        services.AddScoped<IEmailDispatchEligibilityEvaluator, EmailDispatchEligibilityEvaluator>();
        services.AddScoped<IEmailDeliveryDisableImpactReader, EmailDeliveryDisableImpactReader>();
        services.AddScoped<IEmailDeliverySettingsWriter, EmailDeliverySettingsWriter>();
        services.AddScoped<IInternalEmailDeliverySettingsWriter>(provider =>
            new Explore.Application.Operations.InternalEmailPolicyMutationSettingsWriter(
                provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>(),
                selected =>
                {
                    var context = selected.GetRequiredService<ExploreDbContext>();
                    var lease = selected.GetRequiredService<ExploreDbContextExecutionLease>();
                    lease.RequireOriginalOwner(context);
                    if (lease.PolicyPurpose != PolicyMutationPurpose.EmailDelivery || lease.HumanScope is not null
                        || selected.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                            ?.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated) == true)
                        throw new InvalidOperationException("Internal email writes require a selected nonhuman email owner.");
                    return new InternalEmailDeliverySettingsWriter(context,
                        selected.GetRequiredService<ISettingMutationLock>(),
                        selected.GetRequiredService<IUnitOfWork>());
                }));
        if (configureDatabaseComposition)
            services.AddEmailPolicyMutationPersistence(
                PrimaryDatabaseConfiguration.BindRuntime(configuration),
                PrimaryDatabaseConfiguration.BindGlobal(configuration),
                PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.EmailPolicyInstanceMutation),
                PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.EmailPolicyTenantMutation));
        services.AddResourceVisitorSettingsPersistence();
        services.AddScoped<IWebPushDispatchOutboxRepository, WebPushDispatchOutboxRepository>();
        services.AddScoped<IIntegrationSyncOutboxRepository, IntegrationSyncOutboxRepository>();
        services.AddScoped<IIntegrationSyncQueueDiscovery>(provider => new IntegrationSyncQueueDiscovery(
            provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
            provider.GetService<PostgresNativeQueueEnrollment>()));
        services.AddScoped<IQueueDrainHealthRepository, QueueDrainHealthRepository>();
        services.AddScoped<IRegistrationProviderSubmissionWriteQueueRepository, RegistrationProviderSubmissionWriteQueueRepository>();
        services.AddScoped<IRegistrationProviderSubmissionWriteEffectRepository, RegistrationProviderSubmissionWriteEffectRepository>();

        // Webhook Repositories
        services.AddScoped<IWebhookConsumerRepository, WebhookConsumerRepository>();
        services.AddScoped<IWebhookConsumerProviderBindingRepository, WebhookConsumerProviderBindingRepository>();
        services.AddScoped<IWebhookEventTypeRepository, WebhookEventTypeRepository>();
        services.AddScoped<IWebhookEndpointRepository, WebhookEndpointRepository>();
        services.AddScoped<IWebhookMessageRepository, WebhookMessageRepository>();
        services.AddScoped<IWebhookDeliveryAttemptRepository, WebhookDeliveryAttemptRepository>();
        services.AddScoped<IWebhookLocalTargetRepository, WebhookLocalTargetRepository>();
        services.AddScoped<IWebhookLocalDeliveryHealthObserver, WebhookLocalDeliveryHealthObserver>();
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
        services.AddHumanSupportAdministrationDataAdapters();

        // Governance Policy Resolver (deterministic hierarchy walk: Instance → Tenant → Organization)
        services.AddScoped<IPolicyResolver, Services.PolicyResolver>();
        services.AddScoped<INotificationPreferenceResolver, Services.NotificationPreferenceResolver>();

        // Notification Repository
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationIntentRepository, NotificationIntentRepository>();
        services.AddScoped<IRecipientNotificationGraphRepository, NotificationIntentRepository>();

        // Idempotency Repository
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IAiActorContextRepository, AiActorContextRepository>();
        services.AddScoped<IPrivateEventManagementRepository, PrivateEventManagementRepository>();
        services.AddScoped<IRegistrationPrivateReadRepository, RegistrationPrivateReadOperations>();
        services.AddScoped<IEventCreationPublisherRepository, EventCreationPublisherRepository>();
        services.AddScoped<IIdempotencyCleanupTenantDiscoveryRepository, IdempotencyCleanupTenantDiscoveryRepository>();
        services.AddScoped<IIdempotencyCleanupExecutionScopeFactory, IdempotencyCleanupExecutionScopeFactory>();
        services.AddScoped<IAtprotoBootstrapReplayRepository, AtprotoBootstrapReplayRepository>();
        services.AddScoped<IAtprotoTransientStoreRepository, AtprotoTransientStoreRepository>();
        services.AddScoped<IAtprotoTransientAssertionReplayRepository, AtprotoTransientAssertionReplayRepository>();

        // AI Assistant Repositories
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IAiEventDraftRecoveryRepository, AiEventDraftRecoveryRepository>();
        services.AddScoped<IAiConsentGrantRepository, AiConsentGrantRepository>();

        services.AddConfigurationManifestPersistence();

        return services;
    }

    /// <summary>Resolves contexts from their selected workflow before considering the ordinary request pool.</summary>
    internal static IServiceCollection AddLocalIdentityExecutionContextSelection(
        this IServiceCollection services, IdentityDatabaseTopology topology)
    {
        services.TryAddScoped<LocalIdentityExecutionLease>();
        services.TryAddScoped<ILocalIdentityExecutionScopeFactory, LocalIdentityExecutionScopeFactory>();
        services.AddScoped(provider =>
        {
            if (InventoryHoldExpiryExecutionScopeFactory.GetSelectedRuntimeContext(provider) is { } holdExpiry)
                return holdExpiry;
            if (GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider) is { } selected)
                return selected;
            var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
            context.ClearTenantFilterBypass();
            context.TenantContext = provider.GetService<ITenantContext>();
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            context.BindTenantLease();
            return context;
        });
        if (topology == IdentityDatabaseTopology.External)
            services.AddScoped(provider =>
                provider.GetRequiredService<LocalIdentityExecutionLease>().Identity is ExternalIdentityDbContext selected
                    ? selected
                    : new ExternalIdentityDbContext(provider.GetRequiredService<DbContextOptions<ExternalIdentityDbContext>>()));
        return services;
    }

    private static LocalIdentityExecutionLease? GetSelectedLocalIdentityExecution(
        IServiceProvider provider, ExploreDbContext application)
    {
        var lease = provider.GetRequiredService<LocalIdentityExecutionLease>();
        if (lease.Application is null && lease.Identity is null)
            return null;
        lease.RequireCurrent(application, lease.Identity
            ?? throw new UnauthorizedAccessException("The selected Local identity context is missing."));
        return lease;
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

}
