// ABOUTME: Verifies fanout audience execution schema in the rebased PostgreSQL baseline.
// ABOUTME: Proves registration coverage, fenced-run constraints, and filtered uniqueness.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class NotificationFanoutAudienceMigrationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_EnforcesFanoutRunExecutionShape()
    {
        var databaseIdentity = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        await Assert.That(databaseIdentity.Database).StartsWith("recipient_delivery_migration_");
        await Assert.That(databaseIdentity.Host is "127.0.0.1" or "localhost").IsTrue();

        await ResetSharedMigrationDatabaseAsync();
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
        string schema = context.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException("The event model must declare a default schema.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await Assert.That(await ColumnExistsAsync(
            connection,
            schema,
            "event_registrations",
            "coverage_established_at")).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync(
            connection,
            schema,
            "notification_fanout_runs",
            "notification_fanout_occurrences",
            ["tenant_id", "fanout_occurrence_id"])).IsTrue();
        await Assert.That(await ConstraintExistsAsync(connection, schema, "ck_notification_fanout_runs_cursor_pair")).IsTrue();
        await Assert.That(await ConstraintExistsAsync(connection, schema, "ck_notification_fanout_runs_occurrence_lease")).IsTrue();
        await Assert.That(await UniqueIndexExistsAsync(
            connection,
            schema,
            "notification_fanout_runs",
            ["tenant_id", "fanout_occurrence_id"])).IsTrue();
        await Assert.That(await FilteredUniqueIndexContainsAsync(
            connection,
            schema,
            "notification_fanout_runs",
            ["tenant_id", "fanout_kind", "notification_entity_type_id", "entity_id", "source_actor_id"],
            "fanout_occurrence_id IS NULL")).IsTrue();
    }

    private ExploreDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
                warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning);
            });
        builder.EnableServiceProviderCaching(false);
        return new ExploreDbContext(builder.Options);
    }

    private Task ResetSharedMigrationDatabaseAsync() => fixture.ResetAsync();

    private static Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string column) =>
        ExistsAsync(
            connection,
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = @name)
            """,
            schema,
            table,
            column);

    private static Task<bool> ConstraintExistsAsync(NpgsqlConnection connection, string schema, string constraint) =>
        ExistsAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = @name AND connamespace = (SELECT oid FROM pg_namespace WHERE nspname = @schema))",
            schema,
            string.Empty,
            constraint);

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        string schema,
        string table,
        string name)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", name);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ForeignKeyExistsAsync(
        NpgsqlConnection connection,
        string schema,
        string dependentTable,
        string principalTable,
        string[] dependentColumns)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint foreign_key
                JOIN pg_class dependent ON dependent.oid = foreign_key.conrelid
                JOIN pg_namespace dependent_schema ON dependent_schema.oid = dependent.relnamespace
                JOIN pg_class principal ON principal.oid = foreign_key.confrelid
                WHERE foreign_key.contype = 'f'
                  AND dependent_schema.nspname = @schema
                  AND dependent.relname = @dependentTable
                  AND principal.relname = @principalTable
                  AND (SELECT string_agg(attribute.attname, ',' ORDER BY key.ordinality)
                       FROM unnest(foreign_key.conkey) WITH ORDINALITY AS key(attnum, ordinality)
                       JOIN pg_attribute attribute ON attribute.attrelid = foreign_key.conrelid AND attribute.attnum = key.attnum) = @columns)
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("dependentTable", dependentTable);
        command.Parameters.AddWithValue("principalTable", principalTable);
        command.Parameters.AddWithValue("columns", string.Join(',', dependentColumns));
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> UniqueIndexExistsAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string[] columns)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_index index
                JOIN pg_class relation ON relation.oid = index.indrelid
                JOIN pg_namespace relation_schema ON relation_schema.oid = relation.relnamespace
                WHERE relation_schema.nspname = @schema
                  AND relation.relname = @table
                  AND index.indisunique
                  AND (SELECT string_agg(attribute.attname, ',' ORDER BY key.ordinality)
                       FROM unnest(index.indkey) WITH ORDINALITY AS key(attnum, ordinality)
                       JOIN pg_attribute attribute ON attribute.attrelid = index.indrelid AND attribute.attnum = key.attnum) = @columns)
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("columns", string.Join(',', columns));
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> FilteredUniqueIndexContainsAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string[] columns,
        string expected)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_expr(index.indpred, index.indrelid)
            FROM pg_index index
            JOIN pg_class relation ON relation.oid = index.indrelid
            JOIN pg_namespace relation_schema ON relation_schema.oid = relation.relnamespace
            WHERE relation_schema.nspname = @schema
              AND relation.relname = @table
              AND index.indisunique
              AND (SELECT string_agg(attribute.attname, ',' ORDER BY key.ordinality)
                   FROM unnest(index.indkey) WITH ORDINALITY AS key(attnum, ordinality)
                   JOIN pg_attribute attribute ON attribute.attrelid = index.indrelid AND attribute.attnum = key.attnum) = @columns
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("columns", string.Join(',', columns));
        string predicate = (string)await command.ExecuteScalarAsync();
        return predicate?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;
    }
}
