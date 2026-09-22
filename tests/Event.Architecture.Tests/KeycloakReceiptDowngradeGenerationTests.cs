using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Architecture.Tests;

public sealed class KeycloakReceiptDowngradeGenerationTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task DowngradeSql_GuardsUnresolvedReceiptsBeforeDrop(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateContext(provider);
        IMigrationsAssembly migrations =
            context.GetService<IMigrationsAssembly>();
        string[] migrationIds = migrations.Migrations.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        string receiptMigration = migrationIds.Single(id =>
            id.EndsWith(
                "_KeycloakOperationReceipts",
                StringComparison.Ordinal));
        int receiptIndex = Array.IndexOf(migrationIds, receiptMigration);
        string predecessor = migrationIds[receiptIndex - 1];
        IMigrator migrator = context.GetService<IMigrator>();

        string first = migrator.GenerateScript(
            receiptMigration,
            predecessor);
        string second = migrator.GenerateScript(
            receiptMigration,
            predecessor);
        int guardIndex = first.IndexOf(
            "ck_keycloak_receipts_no_unresolved_downgrade",
            StringComparison.OrdinalIgnoreCase);
        int dropIndex = first.IndexOf(
            "DROP TABLE",
            StringComparison.OrdinalIgnoreCase);

        await Assert.That(guardIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(dropIndex).IsGreaterThan(guardIndex);
        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task UpgradeSql_DoesNotInstallDowngradeOnlyGuard(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateContext(provider);
        IMigrationsAssembly migrations =
            context.GetService<IMigrationsAssembly>();
        string[] migrationIds = migrations.Migrations.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        string receiptMigration = migrationIds.Single(id =>
            id.EndsWith(
                "_KeycloakOperationReceipts",
                StringComparison.Ordinal));
        int receiptIndex = Array.IndexOf(migrationIds, receiptMigration);
        string predecessor = migrationIds[receiptIndex - 1];

        string script = context.GetService<IMigrator>()
            .GenerateScript(predecessor, receiptMigration);

        await Assert.That(script).DoesNotContain(
            "ck_keycloak_receipts_no_unresolved_downgrade");
    }

    private static ExploreDbContext CreateContext(
        PrimaryDatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateOptions(provider));
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(
        PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = "keycloak-receipt-architecture.db"
            };
        }

        PrimaryDatabaseServerFlavor? flavor = provider switch
        {
            PrimaryDatabaseProvider.MySql =>
                PrimaryDatabaseServerFlavor.MySql,
            PrimaryDatabaseProvider.MariaDb =>
                PrimaryDatabaseServerFlavor.MariaDb,
            _ => null
        };
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "event_db",
            Username = $"migration-{Guid.CreateVersion7():N}",
            Password = $"password-{Guid.CreateVersion7():N}",
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor switch
            {
                PrimaryDatabaseServerFlavor.MySql => new Version(8, 4),
                PrimaryDatabaseServerFlavor.MariaDb => new Version(11, 4),
                _ => null
            }
        };
    }
}
