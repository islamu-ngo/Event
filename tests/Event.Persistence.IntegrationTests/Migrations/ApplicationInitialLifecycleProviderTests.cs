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
        await Assert.That(migrations.Where(id => id.EndsWith("_Init", StringComparison.Ordinal)))
            .HasSingleItem();
        await Assert.That(migrations[0]).EndsWith("_Init");
        await Assert.That(migrations.Length).IsEqualTo(2);
        string initialMigration = migrations[0];
        await Assert.That(initialMigration).IsEqualTo(databaseOptions.Provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "20260906223112_Init",
            PrimaryDatabaseProvider.Sqlite => "20260906223113_Init",
            PrimaryDatabaseProvider.SqlServer => "20260906223115_Init",
            PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb => "20260906223116_Init",
            _ => throw new ArgumentOutOfRangeException(nameof(databaseOptions))
        });
        string latestMigration = migrations[^1];
        await Assert.That(latestMigration).EndsWith("_EmailOptionalSelfHostingIntegration");

        await migrator.MigrateAsync(initialMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo([initialMigration], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var populated = await PopulatedIntegrationLifecycleData.SeedAsync(context);
        await populated.AssertPreservedAsync(context);

        await migrator.MigrateAsync(latestMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await populated.AssertIntegratedAsync(context);

        await migrator.MigrateAsync(initialMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo([initialMigration], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await populated.AssertPreservedAsync(context);

        await migrator.MigrateAsync(latestMigration);
        await populated.AssertIntegratedAsync(context);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        await migrator.MigrateAsync(Migration.InitialDatabase);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync()).IsEmpty();

        await migrator.MigrateAsync(latestMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(await context.Tenants.AnyAsync(tenant => tenant.Id == populated.TenantId))
            .IsFalse();

        // This is deliberately last: a rejected Down need not leave every provider's DDL atomic.
        await PopulatedIntegrationLifecycleData.AssertLocalBootstrapRollbackRejectedAsync(context, initialMigration);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
