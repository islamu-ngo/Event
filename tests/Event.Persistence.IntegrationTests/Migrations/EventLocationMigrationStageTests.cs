// ABOUTME: PostgreSQL acceptance tests for ordinary Explore database migration behavior after the clean reset.
// ABOUTME: Converts the obsolete staged gate coverage into current-set, retry, and legacy-config invariants.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
[Property("Category", "EventLocationPrivacy")]
public sealed class EventLocationMigrationStageTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task GenericMigrator_AppliesEveryCurrentMigrationWithoutAStageGate()
    {
        await WithDatabaseAsync(async context =>
        {
            await ExploreDatabaseMigrator.MigrateAsync(context, new ConfigurationManager());

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(context.Database.GetMigrations());
        });
    }

    [Test]
    public async Task ReapplyingCurrentMigrationSet_IsSuccessfulNoOp_AndPreservesHistory()
    {
        await WithDatabaseAsync(async context =>
        {
            var configuration = new ConfigurationManager();
            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            string[] historyBeforeRetry = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(historyBeforeRetry);
        });
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("Backfill")]
    [Arguments("Contract")]
    [Arguments("Everything")]
    public async Task LegacyStageConfiguration_DoesNotAlterOrdinaryMigrationBehavior(string? legacyValue)
    {
        await WithDatabaseAsync(async context =>
        {
            var configuration = new ConfigurationManager
            {
                ["Database:Migrations:EventLocationPrivacyStage"] = legacyValue
            };

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(context.Database.GetMigrations());
        });
    }

    private async Task WithDatabaseAsync(Func<ExploreDbContext, Task> action)
    {
        string databaseName = $"normal_migration_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(databaseName);
        try
        {
            await using ExploreDbContext context = CreateContext(connectionString);
            await action(context);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
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
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
            connection);
        terminate.Parameters.AddWithValue("database", databaseName);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static ExploreDbContext CreateContext(string connectionString)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        return new ExploreDbContext(builder.Options);
    }
}
