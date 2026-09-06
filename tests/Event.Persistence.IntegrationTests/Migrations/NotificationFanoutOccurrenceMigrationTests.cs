// ABOUTME: Verifies fanout-occurrence schema in the rebased PostgreSQL baseline.
// ABOUTME: Proves model parity, tenant-safe foreign keys, and the recipient uniqueness guard.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class NotificationFanoutOccurrenceMigrationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_CreatesOccurrenceSchemaAndMatchesModel()
    {
        await ResetSharedMigrationDatabaseAsync();
        await using var context = CreateDbContext();
        IMigrator migrator = context.GetService<IMigrator>();
        string schema = context.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException("The event model must declare a default schema.");
        IForeignKey occurrenceForeignKey = context.Model.FindEntityType(typeof(Explore.Domain.NotificationIntent))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Select(property => property.Name).SequenceEqual(
            ["TenantId", "FanoutOccurrenceId"]));
        IIndex recipientIdentity = context.Model.FindEntityType(typeof(Explore.Domain.NotificationIntent))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
            ["TenantId", "FanoutOccurrenceId", "RecipientUserId"]));
        await Assert.That(occurrenceForeignKey.PrincipalEntityType.ClrType)
            .IsEqualTo(typeof(Explore.Domain.NotificationFanoutOccurrence));
        await Assert.That(occurrenceForeignKey.PrincipalKey.Properties.Select(property => property.Name)
            .SequenceEqual(["TenantId", "Id"])).IsTrue();
        await Assert.That(recipientIdentity.IsUnique).IsTrue();
        await Assert.That(recipientIdentity.GetFilter()).IsNull();

        try
        {
            await migrator.MigrateAsync();
            await Assert.That(ReadPendingModelOperations(context)).IsEmpty();
            await AssertSchemaAsync(
                schema,
                occurrenceForeignKey.GetConstraintName()
                    ?? throw new InvalidOperationException("Fanout occurrence foreign key has no database name."),
                recipientIdentity.GetDatabaseName()
                    ?? throw new InvalidOperationException("Fanout recipient identity index has no database name."));
        }
        finally
        {
            await ResetSharedMigrationDatabaseAsync();
        }
    }

    private Task ResetSharedMigrationDatabaseAsync() => fixture.ResetAsync();

    private ExploreDbContext CreateDbContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        return new ExploreDbContext(builder.Options);
    }

    private static string[] ReadPendingModelOperations(ExploreDbContext context)
    {
        IMigrationsAssembly migrationsAssembly = context.GetService<IMigrationsAssembly>();
        IMigrationsModelDiffer modelDiffer = context.GetService<IMigrationsModelDiffer>();
        IModel runtimeModel = context.GetService<IDesignTimeModel>().Model;
        IModel rawSnapshotModel = migrationsAssembly.ModelSnapshot?.Model
            ?? throw new InvalidOperationException("ExploreDbContext migration snapshot was not found.");
        IModel snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(rawSnapshotModel, designTime: true, validationLogger: null);

        return modelDiffer
            .GetDifferences(snapshotModel.GetRelationalModel(), runtimeModel.GetRelationalModel())
            .Select(DescribeOperation)
            .ToArray();
    }

    private static string DescribeOperation(MigrationOperation operation) => operation switch
    {
        AddColumnOperation value => $"AddColumn:{value.Table}.{value.Name}",
        AlterColumnOperation value => $"AlterColumn:{value.Table}.{value.Name}",
        DropColumnOperation value => $"DropColumn:{value.Table}.{value.Name}",
        CreateIndexOperation value => $"CreateIndex:{value.Table}.{value.Name}",
        DropIndexOperation value => $"DropIndex:{value.Table}.{value.Name}",
        AddForeignKeyOperation value => $"AddForeignKey:{value.Table}.{value.Name}",
        DropForeignKeyOperation value => $"DropForeignKey:{value.Table}.{value.Name}",
        AddUniqueConstraintOperation value => $"AddUnique:{value.Table}.{value.Name}",
        DropUniqueConstraintOperation value => $"DropUnique:{value.Table}.{value.Name}",
        AddCheckConstraintOperation value => $"AddCheck:{value.Table}.{value.Name}",
        DropCheckConstraintOperation value => $"DropCheck:{value.Table}.{value.Name}",
        _ => operation.GetType().Name
    };

    private async Task AssertSchemaAsync(
        string schema,
        string occurrenceForeignKeyName,
        string recipientIdentityIndexName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await Assert.That(await NotificationMigrationSchemaContract.HasTableAsync(
            connection, schema, "notification_fanout_occurrences")).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasColumnAsync(
            connection, schema, "notification_intents", "fanout_occurrence_id")).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasForeignKeyAsync(
            connection, schema, "notification_intents", ["tenant_id", "fanout_occurrence_id"],
            "notification_fanout_occurrences", ["tenant_id", "id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasUniqueIndexAsync(
            connection, schema, "notification_intents",
            ["tenant_id", "fanout_occurrence_id", "recipient_user_id"])).IsTrue();
        await Assert.That(await ExistsAsync(connection, """
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = @name
                  AND connamespace = (SELECT oid FROM pg_namespace WHERE nspname = @schema))
            """, schema, occurrenceForeignKeyName, string.Empty)).IsTrue();
        await Assert.That(await ExistsAsync(connection, """
            SELECT EXISTS (
                SELECT 1
                FROM pg_index relation_index
                JOIN pg_class relation ON relation.oid = relation_index.indrelid
                JOIN pg_class index_relation ON index_relation.oid = relation_index.indexrelid
                JOIN pg_namespace relation_schema ON relation_schema.oid = relation.relnamespace
                WHERE relation_schema.nspname = @schema
                  AND relation.relname = @table
                  AND index_relation.relname = @name
                  AND relation_index.indisunique)
            """, schema, recipientIdentityIndexName, "notification_intents")).IsTrue();
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        string schema,
        string name,
        string table)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Schema existence query returned no value."));
    }
}
