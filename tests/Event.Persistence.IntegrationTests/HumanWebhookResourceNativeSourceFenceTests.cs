using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanWebhookResourceNativeSourceFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedConsumerChangeOrPhantomAfterSnapshotRequiresWholeReprepare(bool phantom)
    {
        var consumer = await SeedAsync();
        if (phantom)
        {
            await using var setup = fixture.CreateDbContext();
            await setup.WebhookConsumers.Where(row => row.Id == consumer.Id).ExecuteDeleteAsync();
        }
        await using var context = Open(consumer.TenantId!.Value);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(context, consumer.Id, acquire: false);
        await using (var writer = fixture.CreateDbContext())
        {
            if (phantom)
            {
                writer.Entry(consumer).State = EntityState.Added;
                await writer.SaveChangesAsync();
            }
            else
                await writer.WebhookConsumers.Where(row => row.Id == consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, 2));
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, consumer.Id, acquire: true));
        await Assert.That(error!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    [Test]
    public async Task ActualRowsAndEmptyRangesAreExcludedWhileOwnPersistedStateIsFresh()
    {
        var consumer = await SeedAsync();
        await using var context = Open(consumer.TenantId!.Value);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var first = JsonDocument.Parse(await ReadAsync(context, consumer.Id, true));
        await Assert.That(first.RootElement.GetProperty("q0")[0].GetProperty("ConfigurationVersion").GetInt32()).IsEqualTo(1);
        await using var writer = fixture.CreateDbContext();
        foreach (string query in new[]
        {
            $"SELECT id FROM {Table(writer, typeof(WebhookConsumer))} WHERE id = '{consumer.Id}' FOR UPDATE NOWAIT",
            $"SELECT id FROM {Epoch(writer)} WHERE id = 1 FOR UPDATE NOWAIT"
        })
        {
            await using var contender = await writer.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => writer.Database.ExecuteSqlRawAsync(query));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await contender.RollbackAsync();
        }
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {Table(context, typeof(WebhookConsumer))} SET status_id = {{0}} WHERE id = {{1}}",
            (int)WebhookConsumerStatus.Archived, consumer.Id);
        using var final = JsonDocument.Parse(await ReadAsync(context, consumer.Id, true));
        await Assert.That(final.RootElement.GetProperty("q0")[0].GetProperty("StatusId").GetInt32())
            .IsEqualTo((int)WebhookConsumerStatus.Archived);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task EveryContributorParticipatesEvenWithoutMatchingRows()
    {
        await SeedAsync();
        Type[] sources =
        [
            typeof(WebhookConsumer), typeof(WebhookEndpoint), typeof(WebhookDeliveryAttempt), typeof(WebhookMessage),
            typeof(WebhookLocalTargetSnapshot), typeof(WebhookDeliveryPlanSnapshot), typeof(WebhookProviderPublication),
            typeof(WebhookBulkReplayOperation), typeof(WebhookConsumerProviderBinding), typeof(InstanceBootstrapState),
            typeof(Organization), typeof(OrganizationTenant), typeof(Group), typeof(GroupTenant), typeof(TenantUser)
        ];
        foreach (Type source in sources)
        {
            await using var writer = fixture.CreateDbContext();
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(writer, source)} WHERE false");
            await using var contender = fixture.CreateDbContext();
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {Epoch(contender)} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
            await transaction.RollbackAsync();
        }
    }

    [Test]
    public async Task RuntimeCanReadExactInstanceLineageOnlyThroughTheDedicatedCapability()
    {
        var tenantConsumer = await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        var instance = await setup.InstanceBootstrapStates.AsNoTracking().OrderByDescending(row => row.Generation).FirstOrDefaultAsync();
        if (instance is null)
        {
            instance = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow);
            setup.InstanceBootstrapStates.Add(instance);
            await setup.SaveChangesAsync();
        }
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.Instance,
            null, instance.Id, null, null, null), $"instance-{Guid.CreateVersion7():N}", WebhookProviderMode.Local, DateTime.UtcNow);
        setup.WebhookConsumers.Add(consumer);
        await setup.SaveChangesAsync();
        await using var runtime = Open(tenantConsumer.TenantId!.Value);
        await runtime.Database.OpenConnectionAsync();
        await using (var ordinary = runtime.Database.GetDbConnection().CreateCommand())
        {
            ordinary.CommandText = $"SELECT count(*) FROM {Table(runtime, typeof(WebhookConsumer))} WHERE id = @id";
            ordinary.Parameters.Add(new NpgsqlParameter("id", consumer.Id));
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await ordinary.ExecuteScalarAsync());
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        using var native = JsonDocument.Parse(await ReadAsync(runtime, consumer.Id, false));
        var row = native.RootElement.GetProperty("q0")[0];
        await Assert.That(row.GetProperty("Id").GetGuid()).IsEqualTo(consumer.Id);
        await Assert.That(row.GetProperty("InstanceId").GetGuid()).IsEqualTo(instance.Id);
        await Assert.That(row.GetProperty("TenantId").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(runtime.TenantFilterTenantId).IsEqualTo(tenantConsumer.TenantId);
    }

    [Test]
    public async Task DisabledWriterCannotSupplyTrustedFactsToTheOtherwiseValidRuntimeCaller()
    {
        var consumer = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string table = Table(migration, typeof(WebhookEndpoint));
        await migration.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {table} DISABLE TRIGGER zz_human_webhook_resource_source_write");
        try
        {
            await using var rejected = Open(consumer.TenantId!.Value);
            var error = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(rejected, consumer.Id, false));
            await Assert.That(error!.SqlState).IsEqualTo("55000");
            await Assert.That(error.MessageText).IsEqualTo("human_webhook_writer_drift");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER zz_human_webhook_resource_source_write");
        }
        await using var context = Open(consumer.TenantId!.Value);
        using var recovered = JsonDocument.Parse(await ReadAsync(context, consumer.Id, false));
        await Assert.That(recovered.RootElement.GetProperty("q0").GetArrayLength()).IsEqualTo(1);
    }

    private async Task<WebhookConsumer> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanSupportAccessSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanWebhookResourceSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Webhook source",
            Slug = $"webhook-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.Tenant,
            tenant.Id, null, null, null, null), "source", WebhookProviderMode.Local, DateTime.UtcNow);
        context.WebhookConsumers.Add(consumer);
        await context.SaveChangesAsync();
        return consumer;
    }

    private static async Task<string> ReadAsync(ExploreDbContext context, Guid consumer, bool acquire)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {(acquire ? PostgresHumanWebhookResourceSourceContract.AcquireFunctionSql(context)
            : PostgresHumanWebhookResourceSourceContract.ObserveFunctionSql(context))}(@actor,1,@tenant,3,@id,NULL,NULL,NULL,NULL)";
        command.Parameters.Add(new NpgsqlParameter("actor", Guid.CreateVersion7()));
        command.Parameters.Add(new NpgsqlParameter("tenant", context.TenantFilterTenantId!.Value));
        command.Parameters.Add(new NpgsqlParameter("id", consumer));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing native facts."));
    }

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
    private static string Epoch(ExploreDbContext context) => context.GetService<ISqlGenerationHelper>()
        .DelimitIdentifier("human_webhook_resource_source_epoch", context.Model.FindEntityType(typeof(User))!.GetSchema());
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
