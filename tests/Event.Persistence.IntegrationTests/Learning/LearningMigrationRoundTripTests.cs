using System.Data.Common;
using Explore.Domain.Learning;
using Explore.Persistence;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Learning;

[Category("LearningProvider")]
public sealed class LearningMigrationRoundTripTests
{
    [Test]
    public async Task GeneratedLearningMigrationAppliesTwiceDownAndUpWithRealSchemaObjects()
    {
        await using var database = await LearningProviderDatabase.CreateAsync(seed: false, migrations: true);
        await using var context = database.OpenSystem();
        string[] migrations = context.Database.GetMigrations().ToArray();
        string learning = migrations.Single(id => id.EndsWith("_AddTenantLearningState", StringComparison.Ordinal));
        int index = Array.IndexOf(migrations, learning);
        await Assert.That(index > 0).IsTrue();
        string before = migrations[index - 1];
        var migrator = context.GetService<IMigrator>();
        await AssertSchemaAsync(context, database.Provider, present: true);
        await migrator.MigrateAsync();
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await AssertSchemaAsync(context, database.Provider, present: true);
        await migrator.MigrateAsync(before);
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations.Take(index), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await AssertSchemaAsync(context, database.Provider, present: false);
        await migrator.MigrateAsync();
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(migrations, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await AssertSchemaAsync(context, database.Provider, present: true);
        await Assert.That(await context.TenantLearningSpaces.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.UserLearningStates.CountAsync()).IsEqualTo(0);
    }

    private static async Task AssertSchemaAsync(
        ExploreDbContext context, PrimaryDatabaseProvider provider, bool present)
    {
        await context.Database.OpenConnectionAsync();
        foreach (Type type in new[] { typeof(TenantLearningSpace), typeof(UserLearningState),
            typeof(LearningTourProgress), typeof(LearningPracticeState) })
        {
            var entity = context.Model.FindEntityType(type)!;
            string table = entity.GetTableName()!;
            string? schema = entity.GetSchema();
            string tableQuery = provider switch
            {
                PrimaryDatabaseProvider.Sqlite => "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @table",
                PrimaryDatabaseProvider.PostgreSql => "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table",
                PrimaryDatabaseProvider.SqlServer => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table",
                PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb =>
                    "SELECT TABLE_NAME FROM information_schema.tables WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table",
                _ => throw new InvalidOperationException("Unsupported Learning schema provider.")
            };
            var tables = await ReadNamesAsync(context, tableQuery, table, schema);
            await Assert.That(tables.Contains(table, StringComparer.Ordinal)).IsEqualTo(present);
            if (!present) continue;

            var store = StoreObjectIdentifier.Table(table, schema);
            var sql = context.GetService<ISqlGenerationHelper>();
            string qualified = sql.DelimitIdentifier(table, schema);
            string columns = string.Join(", ", entity.GetProperties().Select(property =>
                sql.DelimitIdentifier(property.GetColumnName(store)!)));
            await using (var probe = context.Database.GetDbConnection().CreateCommand())
            {
                probe.CommandText = $"SELECT {columns} FROM {qualified} WHERE 1 = 0";
                await using var reader = await probe.ExecuteReaderAsync();
                await Assert.That(reader.FieldCount).IsEqualTo(entity.GetProperties().Count());
            }
            string indexQuery = provider switch
            {
                PrimaryDatabaseProvider.Sqlite => "SELECT name FROM pragma_index_list(@table) WHERE \"unique\" = 1",
                PrimaryDatabaseProvider.PostgreSql => """
                    SELECT i.relname FROM pg_catalog.pg_index x
                    JOIN pg_catalog.pg_class t ON t.oid = x.indrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    JOIN pg_catalog.pg_class i ON i.oid = x.indexrelid
                    WHERE n.nspname = @schema AND t.relname = @table AND x.indisunique
                    """,
                PrimaryDatabaseProvider.SqlServer => """
                    SELECT i.name FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                    JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = @schema AND t.name = @table AND i.is_unique = 1
                    """,
                PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb => """
                    SELECT DISTINCT INDEX_NAME FROM information_schema.statistics
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND NON_UNIQUE = 0
                    """,
                _ => throw new InvalidOperationException("Unsupported Learning index provider.")
            };
            string[] indexes = await ReadNamesAsync(context, indexQuery, table, schema);
            foreach (var unique in entity.GetIndexes().Where(value => value.IsUnique))
                await Assert.That(indexes).Contains(unique.GetDatabaseName()!);
        }
    }

    private static async Task<string[]> ReadNamesAsync(
        ExploreDbContext context, string query, string table, string? schema)
    {
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = query;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "table";
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);
        if (query.Contains("@schema", StringComparison.Ordinal))
        {
            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = "schema";
            schemaParameter.Value = schema ?? throw new InvalidOperationException("A schema-capable provider requires its native model schema.");
            command.Parameters.Add(schemaParameter);
        }
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result.ToArray();
    }
}
