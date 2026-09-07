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
        await Assert.That(await NotificationMigrationSchemaContract.HasForeignKeyAsync(
            connection, schema, "email_dispatch_outbox", ["tenant_id", "notification_intent_id", "recipient_user_id"],
            "notification_intents", ["tenant_id", "id", "recipient_user_id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasForeignKeyAsync(
            connection, schema, "notification_deliveries", ["tenant_id", "notification_id"],
            "notifications", ["tenant_id", "id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasUniqueIndexAsync(
            connection, schema, "notification_deliveries", ["tenant_id", "notification_intent_id", "channel_id"])).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasColumnAsync(
            connection, schema, "notification_intents", "recipient_user_id", required: true)).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasColumnAsync(
            connection, schema, "email_dispatch_outbox", "recipient_user_id", required: true)).IsTrue();
        await Assert.That(await NotificationMigrationSchemaContract.HasColumnAsync(
            connection, schema, "email_dispatch_outbox", "notification_intent_id", required: true)).IsTrue();
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

}
