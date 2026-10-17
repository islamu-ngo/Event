using Explore.Application.Configuration;
using Explore.Persistence.Database;
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
        ExploreDbContext runtimeDatabase,
        IHostEnvironment environment,
        IConfiguration configuration,
        PrimaryDatabaseConnectionOptions migrationDatabaseOptions,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeDatabase);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(migrationDatabaseOptions);
        ArgumentNullException.ThrowIfNull(logger);

        bool agentBrowser = EnsureAgentBrowserAdmission(configuration, environment);
        if (agentBrowser && (migrationDatabaseOptions.Provider != PrimaryDatabaseProvider.PostgreSql
            || migrationDatabaseOptions.Database != "islamu_event_agent"
            || !runtimeDatabase.Database.IsNpgsql()
            || runtimeDatabase.Database.GetDbConnection().Database != "islamu_event_agent"))
            throw new InvalidOperationException("agent_browser_database_binding_mismatch");

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
        await using (var migrationDatabase = new ExploreDbContext(migrationOptions.Options))
        {
            if (agentBrowser)
                await migrationDatabase.Database.ExecuteSqlRawAsync(
                    "CREATE EXTENSION IF NOT EXISTS postgis",
                    cancellationToken);

            await MigrateAsync(migrationDatabase, configuration, cancellationToken);
        }
        logger.LogInformation("Database migration operation {Operation} completed.", "Application");

        await SqliteDatabaseInitializer.InitializeAsync(runtimeDatabase, cancellationToken);
        if (runtimeDatabase.Database.IsNpgsql())
        {
            await PostgresModelConstraintApplier.ApplyAsync(runtimeDatabase, cancellationToken);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(runtimeDatabase, cancellationToken);
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
            await LookupTableSeeder.SeedAsync(runtimeDatabase, cancellationToken);
        else
            await DatabaseSeeder.SeedAsync(
                runtimeDatabase,
                environment,
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

        await db.Database.MigrateAsync(cancellationToken);
        await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(db, cancellationToken);
        await PostgresQueueDrainHealthContract.ApplyAsync(db, cancellationToken);
        await EventDiscoveryRankBackfill.ApplyAsync(db, cancellationToken);
        if (db.Database.IsNpgsql())
        {
            await using var executionDatabase = CreateNamespaceExecutionContext(db);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresRefundReconciliationHealthContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            await PostgresInventoryHoldExpiryDiscoveryContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            await PostgresEmailRetentionDiscoveryContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            await PostgresRegistrationRetentionDiscoveryContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            await PostgresEventResourceAuditRetentionContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            await PostgresIdempotencyReceiptContract.ApplyAsync(
                executionDatabase, PostgresNativeQueueEnrollment.Bind(configuration), cancellationToken);
            var localizationEnrollment = PostgresNativeQueueEnrollment.Bind(configuration);
            await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresPolicySourceSetContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresHumanPrincipalSourceContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresHumanSupportAccessSourceContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresProviderConfigurationContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresOrdinarySettingsContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresLocalizationSecretStatusContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresGlobalLookupCatalogueContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresExternalLoginAuthenticationContract.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresUserTenantMembershipContract.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresTenantPreferenceContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresUserTenantNavigationContract.ApplyAsync(executionDatabase, localizationEnrollment, cancellationToken);
            await PostgresAdministrativeAuthorityContract.ApplyAsync(executionDatabase, cancellationToken);
            await PostgresTenantLookupContract.ApplyAsync(executionDatabase, cancellationToken);
            foreach (string role in new[]
            {
                PostgresExternalLoginAuthenticationContract.GlobalRole,
                PostgresExternalApiKeyAuthenticationContract.GlobalRole,
                PostgresAdmissionScannerAuthenticationContract.GlobalRole,
                PostgresUserTenantMembershipContract.GlobalRole,
                PostgresTenantPreferenceContract.GlobalRole,
                PostgresAdministrativeAuthorityContract.GlobalRole,
                PostgresTenantLookupContract.GlobalRole
            })
                await executionDatabase.Database.ExecuteSqlRawAsync(
                    $"""
                    GRANT "{role}" TO "{localizationEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                    """, cancellationToken);
        }
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
        return new ExploreDbContext(options.Options);
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
