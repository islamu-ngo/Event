using Explore.Domain.Keycloak;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.RegularExpressions;

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
        (string beforeReceipts, string receiptMigration) = GetReceiptMigrationInterval(context);
        IMigrator migrator = context.GetService<IMigrator>();

        string first = migrator.GenerateScript(
            receiptMigration,
            beforeReceipts);
        string second = migrator.GenerateScript(
            receiptMigration,
            beforeReceipts);
        int guardIndex = first.IndexOf(
            "ck_keycloak_receipts_no_unresolved_downgrade",
            StringComparison.OrdinalIgnoreCase);
        Match receiptTableDrop = Regex.Match(
            first,
            @"DROP TABLE[^\r\n]*keycloak_operation_receipts",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        await Assert.That(guardIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(receiptTableDrop.Success).IsTrue();
        await Assert.That(receiptTableDrop.Index).IsGreaterThan(guardIndex);
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
        (string beforeReceipts, string receiptMigration) = GetReceiptMigrationInterval(context);

        string script = context.GetService<IMigrator>()
            .GenerateScript(beforeReceipts, receiptMigration);

        await Assert.That(script).DoesNotContain(
            "ck_keycloak_receipts_no_unresolved_downgrade");
    }

    private static (string BeforeReceipts, string ReceiptMigration) GetReceiptMigrationInterval(
        ExploreDbContext context)
    {
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        string provider = context.Database.ProviderName!;
        var receiptEntity = context.Model.FindEntityType(typeof(KeycloakOperation))!;
        string receiptTable = receiptEntity.GetTableName()!;
        string? receiptSchema = receiptEntity.GetSchema();
        var orderedMigrations = migrations.Migrations.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        int receiptIndex = Enumerable.Range(0, orderedMigrations.Length)
            .Single(index => migrations.CreateMigration(orderedMigrations[index].Value, provider)
                .UpOperations.OfType<CreateTableOperation>()
                .Any(operation => operation.Name == receiptTable && operation.Schema == receiptSchema));

        return (
            receiptIndex == 0 ? Migration.InitialDatabase : orderedMigrations[receiptIndex - 1].Key,
            orderedMigrations[receiptIndex].Key);
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
