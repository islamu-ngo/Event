using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace Event.Architecture.Tests;

public sealed class PrimaryDatabaseMigrationCompositionTests
{
    [Test]
    public async Task SupportedProvidersBuildModelsWithFixedNamespaces()
    {
        var schemaProviders = new List<PrimaryDatabaseProvider>();
        var prefixedProviders = new List<PrimaryDatabaseProvider>();
        var postgresConstraintProviders = new List<PrimaryDatabaseProvider>();

        foreach (var provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            await using var context = CreateContext(provider);
            IModel model = context.Model;
            string[] tableNames = model.GetEntityTypes()
                .Select(entityType => entityType.GetTableName())
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            await Assert.That(tableNames).IsNotEmpty();

            if (model.GetDefaultSchema() is { } schema)
            {
                await Assert.That(schema).IsEqualTo("islamu_event");
                schemaProviders.Add(provider);
            }
            else
            {
                await Assert.That(tableNames)
                    .All(tableName => tableName.StartsWith("ie_", StringComparison.Ordinal));
                prefixedProviders.Add(provider);
            }

            bool hasPostgresConstraints = model.GetEntityTypes()
                .SelectMany(entityType => entityType.GetAnnotations())
                .Any(annotation => annotation.Name.StartsWith(
                    "Explore:PostgresExclusionConstraint:",
                    StringComparison.Ordinal));
            if (hasPostgresConstraints)
            {
                postgresConstraintProviders.Add(provider);
            }
        }

        await Assert.That(schemaProviders)
            .IsEquivalentTo([PrimaryDatabaseProvider.PostgreSql, PrimaryDatabaseProvider.SqlServer]);
        await Assert.That(prefixedProviders)
            .IsEquivalentTo([
                PrimaryDatabaseProvider.Sqlite,
                PrimaryDatabaseProvider.MariaDb,
                PrimaryDatabaseProvider.MySql,
            ]);
        await Assert.That(postgresConstraintProviders)
            .IsEquivalentTo([PrimaryDatabaseProvider.PostgreSql]);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    public async Task RuntimeSchemaCapableModelsUseConfiguredSchema(PrimaryDatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        var options = CreateOptions(provider) with
        {
            Role = PrimaryDatabaseRole.Runtime,
            Schema = "operator_event",
        };
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        await using var context = new ExploreDbContext(builder.Options);

        await Assert.That(context.Model.GetDefaultSchema()).IsEqualTo("operator_event");
        string[] tableNames = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(tableNames).Contains("users");
        await Assert.That(tableNames)
            .All(tableName => !tableName.StartsWith("ie_", StringComparison.Ordinal));
    }

    [Test]
    public async Task PostgresConstraintApplierSkipsSqlite()
    {
        await using var context = CreateContext(PrimaryDatabaseProvider.Sqlite);

        await PostgresModelConstraintApplier.ApplyAsync(context);
    }

    [Test]
    public async Task CompositionRejectsProvidersOutsideTheClosedEnum()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        var options = CreateOptions(PrimaryDatabaseProvider.PostgreSql) with
        {
            Provider = (PrimaryDatabaseProvider)int.MaxValue,
        };

        await Assert.That(() => PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options))
            .Throws<OptionsValidationException>();
    }

    private static ExploreDbContext CreateContext(PrimaryDatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
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
                Database = "architecture-event.db",
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
            Password = "test-only-password",
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor is null ? null : new Version(11, 4),
        };
    }
}
