using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Architecture.Tests;

public sealed class ActorMediaCutoverGenerationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("/api/storageobject/018f0579-5882-7000-8000-000000000001/content")]
    [Arguments("https://foreign.example.test/image.png")]
    public async Task CutoverRejectsUnclassifiedLegacyMediaBeforeRenaming(string? legacyUri)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = "actor-media-cutover-generation.db"
            });
        await using var context = new ExploreDbContext(builder.Options);
        string tableName = context.Model.FindEntityType(typeof(ActorPii))!.GetTableName()!;
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(tableName);
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = $"CREATE TABLE {table} (profile_picture_uri TEXT); INSERT INTO {table} VALUES ($uri);";
            seed.Parameters.AddWithValue("$uri", (object?)legacyUri ?? DBNull.Value);
            await seed.ExecuteNonQueryAsync();
        }

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
            [new RenameColumnOperation
            {
                Table = tableName,
                Name = "profile_picture_uri",
                NewName = "external_profile_picture_uri"
            }], context.Model);
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
        catch (SqliteException exception) when (
            exception.SqliteErrorCode is 1 or 19
            && exception.Message.Contains("CHECK constraint failed", StringComparison.Ordinal))
        {
            rejected = true;
        }
        await Assert.That(rejected).IsEqualTo(legacyUri is not null);
        await transaction.RollbackAsync();
        await using var unchanged = connection.CreateCommand();
        unchanged.CommandText = $"SELECT profile_picture_uri FROM {table};";
        await Assert.That(await unchanged.ExecuteScalarAsync())
            .IsEqualTo((object?)legacyUri ?? DBNull.Value);
    }
}
