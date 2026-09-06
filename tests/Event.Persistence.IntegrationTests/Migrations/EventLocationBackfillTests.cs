// ABOUTME: Converts obsolete Event Location Privacy backfill tests into ordinary EF migration behavior checks.
// ABOUTME: Proves legacy Backfill configuration is inert while current migration history applies and retries safely.

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
public sealed class EventLocationBackfillTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task LegacyBackfillConfiguration_AppliesOrdinaryCurrentMigration()
    {
        await WithDatabaseAsync(async context =>
        {
            var configuration = new ConfigurationManager
            {
                ["Database:Migrations:EventLocationPrivacyStage"] = "Backfill"
            };

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo([Task4MigrationProbe.MigrationId]);
            await Assert.That(await ProbeTableExistsAsync(context)).IsTrue();
        });
    }

    [Test]
    public async Task LegacyBackfillConfiguration_ProducesSameHistoryAsMissingConfiguration()
    {
        string[] withoutLegacyConfiguration = [];
        string[] withLegacyConfiguration = [];

        await WithDatabaseAsync(async context =>
        {
            await ExploreDatabaseMigrator.MigrateAsync(context, new ConfigurationManager());
            withoutLegacyConfiguration = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        });
        await WithDatabaseAsync(async context =>
        {
            var configuration = new ConfigurationManager
            {
                ["Database:Migrations:EventLocationPrivacyStage"] = "Backfill"
            };
            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            withLegacyConfiguration = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        });

        await Assert.That(withLegacyConfiguration).IsEquivalentTo(withoutLegacyConfiguration);
        await Assert.That(withLegacyConfiguration).IsEquivalentTo([Task4MigrationProbe.MigrationId]);
    }

    [Test]
    public async Task LegacyBackfillConfiguration_ReapplyIsSuccessfulNoOp()
    {
        await WithDatabaseAsync(async context =>
        {
            var configuration = new ConfigurationManager
            {
                ["Database:Migrations:EventLocationPrivacyStage"] = "Backfill"
            };
            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            string[] historyBeforeRetry = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(historyBeforeRetry);
            await Assert.That(await ProbeTableExistsAsync(context)).IsTrue();
        });
    }

    private async Task WithDatabaseAsync(Func<ExploreDbContext, Task> action)
    {
        string databaseName = $"normal_backfill_{Guid.NewGuid():N}";
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
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(typeof(Task4MigrationProbe).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        return new Task4MigrationProbeContext(builder.Options);
    }

    private static Task<bool> ProbeTableExistsAsync(ExploreDbContext context)
    {
        return context.Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('public.task4_migration_probe') IS NOT NULL AS "Value" """)
            .SingleAsync();
    }
}
