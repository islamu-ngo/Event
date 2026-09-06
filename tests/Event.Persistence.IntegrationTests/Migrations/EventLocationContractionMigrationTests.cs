// ABOUTME: PostgreSQL acceptance for the generated initial schema's mediated physical venue references.
// ABOUTME: Proves every carrier rejects raw LocationId use and the development baseline rolls back cleanly.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
[Property("Category", "EventLocationPrivacy")]
public sealed class EventLocationContractionMigrationTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    private const string MigrationSuffix = "Init";

    private const string ContractionSql = "location_id IS NULL OR event_location_id IS NOT NULL";

    /// <summary>Carrier types whose finalized relational metadata owns the physical constraint identity.</summary>
    private static readonly Type[] Carriers =
    [
        typeof(EventSession),
        typeof(EventSessionGroup),
        typeof(EventAgendaItem),
        typeof(EventSessionAgendaItem)
    ];

    [Test]
    public async Task ContractionMigration_IsPartOfTheCurrentMigrationSet()
    {
        await WithMigratedDatabaseAsync(async (context, _) =>
        {
            string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            await Assert.That(applied.Any(migration =>
                    migration.EndsWith(MigrationSuffix, StringComparison.Ordinal)))
                .IsTrue();
        });
    }

    [Test]
    public async Task EveryCarrierCarriesTheMediationConstraintAfterMigration()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                bool exists = await ConstraintExistsAsync(connectionString, table, constraint);
                await Assert.That(exists).IsTrue();
            }
        });
    }

    [Test]
    public async Task ZeroGapHolds_NoExistingRowReferencesAPhysicalVenueWithoutAnEventLocation()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            foreach (Type carrier in Carriers)
            {
                (string table, _) = GetPhysicalConstraint(context, carrier);
                long orphans = await ScalarAsync(
                    connectionString,
                    $"""
                     SELECT COUNT(*) FROM islamu_event."{table}"
                     WHERE location_id IS NOT NULL AND event_location_id IS NULL
                     """);

                await Assert.That(orphans).IsEqualTo(0L);
            }
        });
    }

    [Test]
    public async Task ContractionMigration_IsReversibleInDevelopment()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            string[] migrations = context.Database.GetMigrations().ToArray();
            int contractionIndex = Array.FindIndex(
                migrations,
                migration => migration.EndsWith(MigrationSuffix, StringComparison.Ordinal));
            await Assert.That(contractionIndex).IsEqualTo(0);

            await context.Database
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("0");

            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                await Assert.That(await ConstraintExistsAsync(connectionString, table, constraint)).IsFalse();
            }

            // Re-applying restores the guarantee, so a rollback is never a one-way door.
            await context.Database.MigrateAsync();
            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                await Assert.That(await ConstraintExistsAsync(connectionString, table, constraint)).IsTrue();
            }
        });
    }

    private async Task WithMigratedDatabaseAsync(Func<ExploreDbContext, string, Task> action)
    {
        string databaseName = $"elp_contraction_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(databaseName);
        try
        {
            await using ExploreDbContext context = CreateContext(connectionString);
            await ExploreDatabaseMigrator.MigrateAsync(context, new ConfigurationManager());
            await action(context, connectionString);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static async Task<bool> ConstraintExistsAsync(
        string connectionString,
        string table,
        string constraint)
    {
        long count = await ScalarAsync(
            connectionString,
            $"""
             SELECT COUNT(*)
             FROM pg_constraint c
             JOIN pg_class t ON t.oid = c.conrelid
             JOIN pg_namespace n ON n.oid = t.relnamespace
             WHERE c.contype = 'c'
               AND n.nspname = 'islamu_event'
               AND t.relname = '{table}'
               AND c.conname = '{constraint}'
             """);

        return count == 1;
    }

    private static (string Table, string Constraint) GetPhysicalConstraint(
        ExploreDbContext context,
        Type carrier)
    {
        var model = context
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>()
            .Model;
        var entityType = model.FindEntityType(carrier)
            ?? throw new InvalidOperationException($"No EF metadata exists for {carrier.Name}.");
        var constraint = entityType.GetCheckConstraints()
            .Single(candidate => string.Equals(candidate.Sql, ContractionSql, StringComparison.Ordinal));
        return (entityType.GetTableName()!, constraint.Name);
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private ExploreDbContext CreateContext(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = RelationalModelNamespace.DefaultSchema,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        return new ExploreDbContext(builder.Options);
    }

    private async Task<string> CreateDatabaseAsync(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            $"""
             SELECT pg_terminate_backend(pid) FROM pg_stat_activity
             WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()
             """,
            connection);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
