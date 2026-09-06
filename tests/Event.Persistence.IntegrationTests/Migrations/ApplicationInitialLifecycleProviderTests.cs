// ABOUTME: Exercises generated application initials through apply, rollback-to-zero, and reapply.
// ABOUTME: Covers schema-less SQLite and real SQL Server lifecycle behavior at the EF migrator seam.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class SqliteApplicationInitialLifecycleTests
{
    [Test]
    public async Task GeneratedInitial_AppliesRollsBackAndReapplies()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"application-initial-lifecycle-{Guid.CreateVersion7():N}.db");
        try
        {
            await AssertLifecycleAsync(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = path
            });
            await AssertDataProtectionLifecycleAsync(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = path
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static async Task AssertLifecycleAsync(
        PrimaryDatabaseConnectionOptions databaseOptions)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, databaseOptions);
        await using var context = new ExploreDbContext(options.Options);
        IMigrator migrator = context.GetService<IMigrator>();
        string[] migrations = context.Database.GetMigrations().ToArray();
        string latestMigration = migrations[^1];

        await migrator.MigrateAsync(latestMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations);

        await migrator.MigrateAsync(Migration.InitialDatabase);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync()).IsEmpty();

        await migrator.MigrateAsync(latestMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations);
    }

    internal static async Task AssertDataProtectionLifecycleAsync(
        PrimaryDatabaseConnectionOptions databaseOptions)
    {
        var options = TestDbContextOptions.Create<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(options, databaseOptions);
        await using var context = new DataProtectionKeyContext(options.Options);
        IMigrator migrator = context.GetService<IMigrator>();
        string migration = context.Database.GetMigrations().Single();

        await migrator.MigrateAsync(migration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo([migration]);

        await migrator.MigrateAsync(Migration.InitialDatabase);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync()).IsEmpty();

        await migrator.MigrateAsync(migration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo([migration]);
    }
}

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("ApplicationInitialLifecycle")]
public sealed class SqlServerApplicationInitialLifecycleTests(
    AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task GeneratedInitial_AppliesRollsBackAndReapplies(
        PrimaryDatabaseProvider provider)
    {
        PrimaryDatabaseConnectionOptions source =
            fixture.CreateOptions(provider);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = source.Provider,
            Host = source.Host,
            Port = source.Port,
            Database = source.Database,
            Schema = source.Schema,
            Username = source.Username,
            Password = source.Password,
            TlsMode = source.TlsMode,
            TrustServerCertificate = source.TrustServerCertificate,
            ServerFlavor = source.ServerFlavor,
            ServerVersion = source.ServerVersion
        };
        await SqliteApplicationInitialLifecycleTests.AssertLifecycleAsync(options);
        await SqliteApplicationInitialLifecycleTests.AssertDataProtectionLifecycleAsync(options);
    }
}
