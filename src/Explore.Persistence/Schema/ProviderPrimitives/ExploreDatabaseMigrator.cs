using Explore.Application.Configuration;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Schema;

public static class ExploreDatabaseMigrator
{
    public static bool EnsureAgentBrowserAdmission(IConfiguration configuration, IHostEnvironment environment, bool isStandaloneHost = false) =>
        new AgentBrowserProvisioningOptions(
            configuration.GetValue<bool>("AGENT_BROWSER_SEED_ENABLED"),
            environment.EnvironmentName,
            configuration["ISLAMU_ASPIRE_MODE"] ?? "",
            configuration["Hosting:Topology"] ?? "",
            configuration["IdentityDatabase:Topology"] ?? configuration["IDENTITY_DATABASE_TOPOLOGY"] ?? "colocated",
            configuration["Authentication:Provider"] ?? configuration["AUTHENTICATION_PROVIDER"] ?? "",
            configuration["Authorization:Provider"] ?? configuration["AUTHORIZATION_PROVIDER"] ?? "",
            configuration["Database:Provider"] ?? configuration["DATABASE_PROVIDER"] ?? "",
            configuration["Database:Database"] ?? configuration["DATABASE_NAME"] ?? "",
            configuration["CONFIGURATION_MANIFEST_MODE"] ?? "Off",
            configuration["PrivacyErasure:Authority:Topology"] ?? configuration["ERASURE_DATABASE_TOPOLOGY"] ?? "",
            configuration["Webhooks:Provider"] ?? configuration["WEBHOOKS_PROVIDER"] ?? "").EnsureAdmitted(
                isStandaloneHost,
                configuration["ERASURE_DATABASE_TOPOLOGY"],
                configuration["WEBHOOKS_PROVIDER"]);

    public static async Task MigrateAndSeedAsync(
        IHostEnvironment environment,
        IConfiguration configuration,
        PrimaryDatabaseConnectionOptions migrationDatabaseOptions,
        ILogger logger,
        bool seedDevelopmentData = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(migrationDatabaseOptions);
        ArgumentNullException.ThrowIfNull(logger);

        bool agentBrowser = EnsureAgentBrowserAdmission(configuration, environment);
        if (agentBrowser && (migrationDatabaseOptions.Provider != PrimaryDatabaseProvider.PostgreSql
            || migrationDatabaseOptions.Database != "islamu_event_agent"))
            throw new InvalidOperationException("agent_browser_database_binding_mismatch");

        var writerEnrollment = migrationDatabaseOptions.Provider == PrimaryDatabaseProvider.PostgreSql
            ? BindLocalIdentityWriterEnrollment(configuration) : null;

        PrivacyErasureAuthorityTopology topology =
            PrivacyErasureDurabilityOptions.GetTopology(configuration);
        if (topology == PrivacyErasureAuthorityTopology.ExternalDatabase)
        {
            PrimaryDatabaseConnectionOptions authorityDatabaseOptions =
                PrivacyErasureAuthorityDatabaseConfiguration.BindMigrator(configuration);
            PrivacyErasureAuthorityDatabaseConfiguration.EnsureDistinctPhysicalDatabase(
                migrationDatabaseOptions,
                authorityDatabaseOptions);
        }

        logger.LogInformation("Applying database migrations...");
        var migrationOptions = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            migrationOptions,
            migrationDatabaseOptions);
        await using var migrationDatabase = new ExploreDbContext(migrationOptions.Options);
        if (agentBrowser)
            await migrationDatabase.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS postgis",
                cancellationToken);

        await MigrateCoreAsync(migrationDatabase, configuration, cancellationToken);
        logger.LogInformation("Database migration operation {Operation} completed.", "Application");

        await using var executionDatabase = CreateNamespaceExecutionContext(migrationDatabase);
        await SqliteDatabaseInitializer.InitializeAsync(executionDatabase, cancellationToken);
        if (executionDatabase.Database.IsNpgsql())
        {
            await PostgresModelConstraintApplier.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(executionDatabase, cancellationToken);
            if (writerEnrollment is not null)
            {
                await PostgresLocalIdentityWriterPermissions.ApplyAsync(executionDatabase, writerEnrollment,
                    IdentityDatabaseConfiguration.GetTopology(configuration),
                    LocalIdentityWriterInventory.Store.Application, cancellationToken);
            }
        }
        logger.LogInformation("Database migration operation {Operation} completed.", "ProviderAdjustments");

        var dataProtectionOptions = new DbContextOptionsBuilder<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(
            dataProtectionOptions,
            migrationDatabaseOptions);
        await using (var dataProtectionDatabase = new DataProtectionKeyContext(dataProtectionOptions.Options))
        {
            await dataProtectionDatabase.Database.MigrateAsync(cancellationToken);
        }
        logger.LogInformation("Database migration operation {Operation} completed.", "DataProtection");

        await MigratePrivacyErasureAuthorityAsync(
            configuration,
            migrationDatabaseOptions,
            topology,
            logger,
            cancellationToken);

        if (agentBrowser)
            await LookupTableSeeder.SeedAsync(executionDatabase, cancellationToken);
        else
            await DatabaseSeeder.SeedAsync(
                executionDatabase,
                environment,
                logger,
                seedDevelopmentData,
                configuration: configuration,
                cancellationToken: cancellationToken);
        logger.LogInformation("Database migration operation {Operation} completed.", "Seed");
        logger.LogInformation("Database migrations and seeding completed successfully.");
    }

    public static async Task MigrateAsync(
        ExploreDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(configuration);

        if (db.Database.IsNpgsql())
            _ = BindLocalIdentityWriterEnrollment(configuration);
        await MigrateCoreAsync(db, configuration, cancellationToken);
    }

    private static async Task MigrateCoreAsync(
        ExploreDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        var mapping = db.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>();
        await using var mappedDatabase = mapping is not null && mapping.ModelSchema != mapping.TargetSchema
            ? CreateNamespaceExecutionContext(db)
            : null;
        var executionDatabase = mappedDatabase ?? db;
        PostgresNativeQueueEnrollment? queueEnrollment = executionDatabase.Database.IsNpgsql()
            ? PostgresNativeQueueEnrollment.Bind(configuration)
            : null;
        await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresTenantLookupContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresDiscoveryDisclosureContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresDiscoveryTraversalContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPublicEventDiscoveryContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPublicEventDetailContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPrivateEventManagementContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresRegistrationPrivateReadContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPublicEventResponseClockContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresLookupCatalogueReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresCategoryReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresAtprotoFederationSettingPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresIdempotencyReceiptContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresAiActorContextReadContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresEventCreationPublisherReadContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresAiConversationOwnerFenceContract.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
        }
        await PostgresSubjectPrivacyErasureContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresPdsSettlementContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresPdsQueueAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresAtprotoModerationCompensationContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresIntegrationSyncReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        await PostgresEmailDispatchAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresAtprotoMaterializationContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresTenantApiKeyStoragePermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        }
        await PostgresInstanceApiKeyManagementContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresInstanceApiKeyIssuanceContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresPlatformApiKeyUsageReportContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresMachineMutationAuthorityContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresMachineRoomMutationAuthorityContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresUserTenantMembershipReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresExternalLoginReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        await PostgresAdministrativeAuthorityContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresActorConsolidationContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresManagedTenantDomainContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresNotificationFanoutAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresNotificationOutboxAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresGenericOutboxAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresOutboxExecutionContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPublicationFanoutExecutionContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        }
        await PostgresWebPushDispatchAdmissionContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresQueueDrainHealthContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresLocationPrivacyReviewContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresLocationPrivacyGovernanceContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresManifestReplayContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresConfigurationManifestBootstrapContract.ApplyAsync(
                executionDatabase, queueEnrollment.GlobalLogin, cancellationToken);
            await executionDatabase.Database.ExecuteSqlRawAsync(
                $"""
                GRANT "{PostgresConfigurationManifestBootstrapContract.GlobalRole}"
                TO "{queueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """, cancellationToken);
        }
        await PostgresConfigurationImportSnapshotContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportStateContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportLegalSourceContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportLegalMutationContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportPublicationPolicyContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportScalarSettingContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportReceiptContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportPaidPolicyContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportTenantIdentityContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportBrandingContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportEventResourceContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresConfigurationImportTenantSettingContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresIncomingWebhookQueueContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        await PostgresEventReportOperationsContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresEmailRetentionReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresEventResourceAuditReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresWebhookLocalDeliveryAdmissionContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresWebhookClaimEndpointStateContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPolicySourceSetContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanPrincipalSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanAuthorizationRouteContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanEventAssignmentSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanSupportAccessSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            if (IdentityDatabaseConfiguration.GetTopology(configuration) == IdentityDatabaseTopology.Colocated)
            {
                await PostgresLocalAdministrationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
                await PostgresSupportAdministrationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
                await PostgresManagedProviderProvisioningContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            }
            await PostgresHumanEmailSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPlatformApiKeySourceContract.ApplyAsync(
                executionDatabase, PlatformApiKeySourceEnrollment.Bind(configuration), cancellationToken);
            await PostgresHumanWebhookResourceSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresHumanEventProviderSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresRegistrationFinalizationQueueContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresPaymentReconciliationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresCheckoutDispatchContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresRefundReconciliationHealthContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresTicketingRecoveryHealthContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        }
        await PostgresVisitorPolicyAuthorityContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresEmailDeliveryPolicyContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresOperatorIdentityEditorContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresResolverConfigurationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresStorageProbeSourceContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresStorageConfigurationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresProviderConfigurationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresMixedSettingsContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresOrdinarySettingsContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresTenantPreferencePermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresUserTenantNavigationReadPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresEventResourceProviderContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        }
        await PostgresPaidPolicyCeilingContract.ApplyAsync(executionDatabase, cancellationToken);
        await PostgresInstanceStorageAdministrationContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
            await PostgresStorageReconciliationContract.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
        await PostgresLocalStorageQuarantineContract.ApplyAsync(executionDatabase, cancellationToken);
        if (queueEnrollment is not null)
        {
            await PostgresDiscoveryMetadataPermissions.ApplyAsync(executionDatabase, queueEnrollment, cancellationToken);
            await PostgresTenantMembershipMutationPermissions.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresActorOwnershipReadPermissions.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresTenantCreationPermissions.ApplyAsync(executionDatabase, queueEnrollment,
                IdentityDatabaseConfiguration.GetTopology(configuration), cancellationToken);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(
                executionDatabase, queueEnrollment, cancellationToken);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(executionDatabase, queueEnrollment,
                IdentityDatabaseConfiguration.GetTopology(configuration), cancellationToken);
        }
        await EventDiscoveryRankBackfill.ApplyAsync(executionDatabase, cancellationToken);
    }

    private static PostgresLocalIdentityWriterEnrollment? BindLocalIdentityWriterEnrollment(
        IConfiguration configuration)
    {
        if (!PrimaryDatabaseConfiguration.TryBindUsername(configuration,
                PrimaryDatabaseRole.LocalIdentityWriter, out string? writerLogin))
            return null;

        var enrollment = new PostgresLocalIdentityWriterEnrollment(
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Migrator),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Runtime),
            writerLogin!,
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Global));
        enrollment.Validate();
        return enrollment;
    }

    private static ExploreDbContext CreateNamespaceExecutionContext(ExploreDbContext migrationDatabase)
    {
        var originalOptions = migrationDatabase.GetService<IDbContextOptions>();
        var options = new DbContextOptionsBuilder<ExploreDbContext>(
            (DbContextOptions<ExploreDbContext>)originalOptions);
        if (originalOptions.FindExtension<RelationalNamespaceOptionsExtension>() is { } mapping)
        {
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(mapping.TargetSchema, mapping.TargetSchema));
        }
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task MigratePrivacyErasureAuthorityAsync(
        IConfiguration configuration,
        PrimaryDatabaseConnectionOptions migrationDatabaseOptions,
        PrivacyErasureAuthorityTopology topology,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (topology == PrivacyErasureAuthorityTopology.ExternalDatabase)
        {
            var authorityDatabase = PrivacyErasureAuthorityDatabaseConfiguration
                .ResolveMigratorConnectionString(configuration);
            var externalAuthorityOptions = new DbContextOptionsBuilder<PrivacyErasureAuthorityDbContext>();
            externalAuthorityOptions.UseNpgsql(authorityDatabase.ConnectionString, npgsql => npgsql
                    .MigrationsAssembly(typeof(PrivacyErasureAuthorityDbContext).Assembly.FullName)
                    .MigrationsHistoryTable(
                        PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable))
                .UseSnakeCaseNamingConvention();
            await using var externalAuthorityDb = new PrivacyErasureAuthorityDbContext(externalAuthorityOptions.Options);
            await externalAuthorityDb.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql,
                cancellationToken);
            await externalAuthorityDb.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql,
                cancellationToken);
            await externalAuthorityDb.Database.MigrateAsync(cancellationToken);
            await ApplyExternalPrivacyErasureAuthorityContractAsync(
                externalAuthorityDb,
                cancellationToken);
            logger.LogInformation(
                "Database migration operation {Operation} completed.",
                "AuthorityExternalDatabasePostgreSql");
            return;
        }

        if (topology == PrivacyErasureAuthorityTopology.CoLocated)
        {
            if (migrationDatabaseOptions.Provider == PrimaryDatabaseProvider.Sqlite)
            {
                var sqliteAuthorityOptions = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>();
                EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(
                    sqliteAuthorityOptions,
                    migrationDatabaseOptions);
                await using var sqliteAuthorityDb = new EmbeddedPrivacyErasureAuthorityDbContext(sqliteAuthorityOptions.Options);
                await sqliteAuthorityDb.Database.MigrateAsync(cancellationToken);
                logger.LogInformation(
                    "Database migration operation {Operation} completed.",
                    "AuthorityCoLocatedSqlite");
                return;
            }

            if (migrationDatabaseOptions.Provider != PrimaryDatabaseProvider.PostgreSql)
            {
                throw new InvalidOperationException(
                    PrimaryDatabaseProviderComposition.UnsupportedCoLocatedPrivacyErasureAuthorityMessage);
            }

            var postgresAuthorityOptions = new DbContextOptionsBuilder<CoLocatedPrivacyErasureAuthorityDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
                postgresAuthorityOptions,
                migrationDatabaseOptions);
            await using var postgresAuthorityDb = new CoLocatedPrivacyErasureAuthorityDbContext(postgresAuthorityOptions.Options);
            await postgresAuthorityDb.Database.MigrateAsync(cancellationToken);
            logger.LogInformation(
                "Database migration operation {Operation} completed.",
                "AuthorityCoLocatedPostgreSql");
            return;
        }

        EmbeddedPrivacyErasureAuthorityOptions embedded =
            EmbeddedPrivacyErasureAuthorityOptions.Bind(configuration);
        using var storage = new EmbeddedPrivacyErasureAuthorityStorage(embedded);
        await storage.EnsureReadyAsync(cancellationToken);
        var embeddedOptions = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>();
        EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(embeddedOptions, embedded);
        await using var embeddedDb = new EmbeddedPrivacyErasureAuthorityDbContext(embeddedOptions.Options);
        await embeddedDb.Database.MigrateAsync(cancellationToken);
        storage.HardenCompanionFiles();
        await storage.VerifyIntegrityAsync(cancellationToken);
        logger.LogInformation(
            "Database migration operation {Operation} completed.",
            "AuthorityEmbeddedSqlite");
    }

    public static async Task ApplyExternalPrivacyErasureAuthorityContractAsync(
        PrivacyErasureAuthorityDbContext authorityDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorityDatabase);

        await using var lifecycleTransaction = await authorityDatabase.Database
            .BeginTransactionAsync(cancellationToken);
        await authorityDatabase.Database.ExecuteSqlRawAsync(
            PrivacyErasureAuthorityDatabaseContract.AuthorityObjectsSql,
            cancellationToken);
        await authorityDatabase.Database.ExecuteSqlRawAsync(
            PrivacyErasureAuthorityDatabaseContract.MigrationSql,
            cancellationToken);
        await authorityDatabase.Database.ExecuteSqlRawAsync(
            PrivacyErasureAuthorityDatabaseContract.RetentionLifecycleMigrationSql,
            cancellationToken);
        await authorityDatabase.Database.ExecuteSqlRawAsync(
            PrivacyIdentityFenceDatabaseContract.MigrationSql,
            cancellationToken);
        await authorityDatabase.Database.ExecuteSqlRawAsync(
            PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql,
            cancellationToken);
        await lifecycleTransaction.CommitAsync(cancellationToken);
    }
}
