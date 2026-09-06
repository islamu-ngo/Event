// ABOUTME: Verifies every supported primary provider owns the generated configuration-manifest audit migration.
// ABOUTME: Compares each provider snapshot with the current model without connecting to an external database.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed class ConfigurationManifestAuditProviderMigrationTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderHeadContainsAuditTablesAndMatchesCurrentModel(
        PrimaryDatabaseProvider provider)
    {
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder,
            CreateOptions(provider));
        await using var context = new ExploreDbContext(optionsBuilder.Options);

        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType operation = model.FindEntityType(typeof(ConfigurationManifestOperation))!;
        IEntityType result = model.FindEntityType(typeof(ConfigurationManifestTenantResult))!;
        IEntityType identity = model.FindEntityType(typeof(AtprotoIdentity))!;
        IProperty did = identity.FindProperty(nameof(AtprotoIdentity.Did))!;
        string expectedPrefix = provider == PrimaryDatabaseProvider.PostgreSql
            || provider == PrimaryDatabaseProvider.SqlServer
                ? string.Empty
                : "ie_";
        string? expectedDidCollation = provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "C",
            PrimaryDatabaseProvider.Sqlite => "BINARY",
            PrimaryDatabaseProvider.SqlServer =>
                "Latin1_General_100_BIN2",
            PrimaryDatabaseProvider.MariaDb or
                PrimaryDatabaseProvider.MySql => "ascii_bin",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                null),
        };

        await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
        await Assert.That(operation.GetTableName())
            .IsEqualTo($"{expectedPrefix}configuration_manifest_operations");
        await Assert.That(result.GetTableName())
            .IsEqualTo($"{expectedPrefix}configuration_manifest_tenant_results");
        await Assert.That(did.ClrType).IsEqualTo(typeof(string));
        await Assert.That(did.IsNullable).IsFalse();
        await Assert.That(did.GetMaxLength()).IsEqualTo(2048);
        await Assert.That(did.GetCollation()).IsEqualTo(expectedDidCollation);
        await Assert.That(identity.GetIndexes().Any(index =>
                index.IsUnique
                && index.GetFilter() is null
                && index.Properties.Count == 1
                && index.Properties[0] == did))
            .IsTrue();
        await Assert.That(identity.FindDeclaredQueryFilter(
                QueryFilterNames.SoftDelete))
            .IsNotNull();
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
                Database = Path.Combine(
                    Path.GetTempPath(),
                    $"configuration-manifest-model-{Guid.CreateVersion7():N}.db")
            };
        }

        string ephemeralCredential = Guid.CreateVersion7().ToString("N");
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "localhost",
            Database = "configuration_manifest_model",
            Username = ephemeralCredential,
            Password = ephemeralCredential,
            TlsMode = PrimaryDatabaseTlsMode.Prefer,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null
            }
        };
    }
}
