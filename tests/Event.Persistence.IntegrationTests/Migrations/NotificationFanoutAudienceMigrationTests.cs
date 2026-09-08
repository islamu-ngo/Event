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
        await Assert.That(await NotificationMigrationSchemaContract.HasColumnAsync(
            connection, schema, "event_registrations", "coverage_established_at")).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasForeignKeyAsync(
            connection, schema, "notification_fanout_runs", ["tenant_id", "fanout_occurrence_id"],
            "notification_fanout_occurrences", ["tenant_id", "id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasCheckAsync(
            connection, schema, "notification_fanout_runs", "ck_notification_fanout_runs_cursor_pair")).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasCheckAsync(
            connection, schema, "notification_fanout_runs", "ck_notification_fanout_runs_occurrence_lease")).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasUniqueIndexAsync(
            connection, schema, "notification_fanout_runs", ["tenant_id", "fanout_occurrence_id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasUniqueIndexAsync(
            connection, schema, "notification_fanout_runs",
            ["tenant_id", "fanout_kind", "notification_entity_type_id", "entity_id", "source_actor_id"],
            "fanout_occurrence_id IS NULL")).IsTrue();
    }

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

    private Task ResetSharedMigrationDatabaseAsync() => fixture.ResetAsync();

}
