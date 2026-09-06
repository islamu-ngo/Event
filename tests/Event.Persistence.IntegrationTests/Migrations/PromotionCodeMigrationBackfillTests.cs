// ABOUTME: Verifies generated initial migrations contain the final registration money snapshot schema.
// ABOUTME: Guards the development rebaseline across every supported primary database provider.

using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class PromotionCodeMigrationBackfillTests
{
    [Test]
    public async Task InitialApplicationScriptsCreateFinalMoneySnapshotColumnsForEveryProvider()
    {
        foreach (PrimaryDatabaseProvider provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            await using var context = CreateContext(provider);
            string script = context.GetService<IMigrator>().GenerateScript(
                fromMigration: null,
                toMigration: null,
                options: MigrationsSqlGenerationOptions.Default);

            await Assert.That(script).Contains("pre_discount_organizer_directed_total_minor_snapshot");
            await Assert.That(script).Contains("post_discount_organizer_directed_total_minor_snapshot");
            await Assert.That(script).Contains("organizer_directed_total_minor_snapshot");
            await Assert.That(script).Contains("pre_discount_line_subtotal_minor_snapshot");
            await Assert.That(script).Contains("post_discount_line_subtotal_minor_snapshot");
            await Assert.That(script).Contains("line_subtotal_snapshot");
        }
    }

    private static ExploreDbContext CreateContext(PrimaryDatabaseProvider provider)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = "persistence-migration-test.db",
            };
        }

        PrimaryDatabaseServerFlavor? flavor =
            Enum.TryParse(provider.ToString(), out PrimaryDatabaseServerFlavor parsedFlavor)
                ? parsedFlavor
                : null;
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "event_db",
            Username = "migration_user",
            Password = Guid.CreateVersion7().ToString("N"),
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor is null ? null : new Version(11, 4),
        };
    }
}
