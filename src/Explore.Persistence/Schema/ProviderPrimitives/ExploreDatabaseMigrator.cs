using Explore.Application.Configuration;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Schema;

public static class ExploreDatabaseMigrator
{
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
            PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql,
            cancellationToken);
        await lifecycleTransaction.CommitAsync(cancellationToken);
    }
}
