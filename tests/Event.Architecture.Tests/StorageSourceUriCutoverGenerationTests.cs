using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Architecture.Tests;

public sealed class StorageSourceUriCutoverGenerationTests
{
    [Test]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments("/api/storageobject/018f0579-5882-7000-8000-000000000001/content", false)]
    [Arguments("https://foreign.example.test/image.png", false)]
    [Arguments("tenants/private/provider-key", false)]
    [Arguments("/api/storageobject/018f0579-5882-7000-8000-000000000001/content", true)]
    [Arguments("https://foreign.example.test/image.png", true)]
    [Arguments("tenants/private/provider-key", true)]
    [Arguments(null, true)]
    [Arguments("", true)]
    [Arguments("   ", false)]
    [Arguments("   ", true)]
    [Arguments("\0", false)]
    [Arguments("\0", true)]
    public async Task NativeCutoverRejectsUnclassifiedLocatorsAndNormalizesAbsentOrigin(string? legacyUri, bool dropAndAdd)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = "storage-source-cutover-generation.db"
            });
        await using var context = new ExploreDbContext(builder.Options);
        string tableName = context.Model.FindEntityType(typeof(StorageObject))!.GetTableName()!;
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(tableName);
        await using var cutoverSchema = new CutoverSchemaContext(
            new DbContextOptionsBuilder<CutoverSchemaContext>().UseSqlite(connection).Options, tableName);
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = $"CREATE TABLE {table} (id INTEGER PRIMARY KEY, uri TEXT); INSERT INTO {table} VALUES (1, $uri);";
            seed.Parameters.AddWithValue("$uri", (object?)legacyUri ?? DBNull.Value);
            await seed.ExecuteNonQueryAsync();
        }
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
            dropAndAdd
                ? [new DropColumnOperation { Table = tableName, Name = "uri" },
                    new AddColumnOperation
                    {
                        Table = tableName, Name = "source_uri", ClrType = typeof(string),
                        ColumnType = "TEXT", IsNullable = true
                    }]
                : [new RenameColumnOperation { Table = tableName, Name = "uri", NewName = "source_uri" }],
            cutoverSchema.GetService<IDesignTimeModel>().Model);
        bool rejected = false;
        await using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var generated in commands)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = generated.CommandText;
                await command.ExecuteNonQueryAsync();
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 1 or 19
            && exception.Message.Contains("CHECK constraint failed", StringComparison.Ordinal))
        {
            rejected = true;
        }
        await Assert.That(rejected).IsEqualTo(!string.IsNullOrEmpty(legacyUri));
        if (!rejected)
        {
            await using var origin = connection.CreateCommand();
            origin.Transaction = transaction;
            origin.CommandText = $"SELECT source_uri FROM {table};";
            await Assert.That(await origin.ExecuteScalarAsync()).IsEqualTo(DBNull.Value);
        }
        await transaction.RollbackAsync();
        await using var unchanged = connection.CreateCommand();
        unchanged.CommandText = $"SELECT uri FROM {table};";
        await Assert.That(await unchanged.ExecuteScalarAsync()).IsEqualTo((object?)legacyUri ?? DBNull.Value);
    }

    private sealed class CutoverSchemaContext(DbContextOptions<CutoverSchemaContext> options, string tableName)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<CutoverRow>().ToTable(tableName);
            builder.Entity<CutoverRow>().Property(row => row.SourceUri).HasColumnName("source_uri");
            builder.Entity<CutoverRow>().Property(row => row.Id).HasColumnName("id");
        }
    }

    private sealed class CutoverRow
    {
        public int Id { get; set; }
        public string? SourceUri { get; set; }
    }
}
