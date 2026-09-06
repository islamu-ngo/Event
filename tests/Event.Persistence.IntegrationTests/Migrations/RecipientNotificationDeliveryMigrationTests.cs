// ABOUTME: Verifies recipient-delivery ledger schema in the rebased PostgreSQL baseline.
// ABOUTME: Proves model parity and required recipient constraints without deleted migration boundaries.

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
public sealed class RecipientNotificationDeliveryMigrationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_MatchesModelAndEnforcesRecipientDeliveryShape()
    {
        var connectionIdentity = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        await Assert.That(connectionIdentity.Database).StartsWith("recipient_delivery_migration_");
        await Assert.That(connectionIdentity.Host is "127.0.0.1" or "localhost").IsTrue();

        await ResetSharedMigrationDatabaseAsync();
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
        await Assert.That(HasPendingModelChanges(context)).IsFalse();
        string schema = context.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException("The event model must declare a default schema.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await Assert.That(await ForeignKeyExistsAsync(
            connection,
            schema,
            "email_dispatch_outbox",
            "notification_intents",
            ["tenant_id", "notification_intent_id", "recipient_user_id"])).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync(
            connection,
            schema,
            "notification_deliveries",
            "notifications",
            ["tenant_id", "notification_id"])).IsTrue();
        await Assert.That(await UniqueIndexExistsAsync(
            connection,
            schema,
            "notification_deliveries",
            ["tenant_id", "notification_intent_id", "channel_id"])).IsTrue();
        await Assert.That(await IsColumnRequiredAsync(connection, schema, "notification_intents", "recipient_user_id")).IsTrue();
        await Assert.That(await IsColumnRequiredAsync(connection, schema, "email_dispatch_outbox", "recipient_user_id")).IsTrue();
        await Assert.That(await IsColumnRequiredAsync(connection, schema, "email_dispatch_outbox", "notification_intent_id")).IsTrue();
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

    private static bool HasPendingModelChanges(ExploreDbContext context)
    {
        IMigrationsAssembly migrationsAssembly = context.GetService<IMigrationsAssembly>();
        IMigrationsModelDiffer modelDiffer = context.GetService<IMigrationsModelDiffer>();
        IModel runtimeModel = context.GetService<IDesignTimeModel>().Model;
        IModel rawSnapshotModel = migrationsAssembly.ModelSnapshot?.Model
            ?? throw new InvalidOperationException("ExploreDbContext migration snapshot was not found.");
        IModel snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(rawSnapshotModel, designTime: true, validationLogger: null);

        return modelDiffer.HasDifferences(
            snapshotModel.GetRelationalModel(),
            runtimeModel.GetRelationalModel());
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

    private static async Task<bool> IsColumnRequiredAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string column)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT is_nullable = 'NO'
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table AND column_name = @column
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
