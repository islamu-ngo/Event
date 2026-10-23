using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    [Arguments("registration")]
    [Arguments("bootstrap")]
    [Arguments("plan")]
    [Arguments("version")]
    [Arguments("setting")]
    [Arguments("quota")]
    public async Task ExplicitWorkerSourceWritesRequireOriginalEnrollmentBeforeAnySourceDml(string sourceKind)
    {
        await using var store = await Store.CreateAsync();
        object source = WorkerSource(sourceKind);
        var commands = new WorkerSourceDmlObserver(source.GetType());
        await using (var writer = store.Open(store.TenantId, commands))
        {
            var connector = writer.Database.GetDbConnection();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            writer.Add(source);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());

            await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_gate_missing");
            await Assert.That(commands.SourceCommands).IsEqualTo(0);
            await RequireOriginalOwnerAsync(writer, connector, transaction);
            await transaction.RollbackAsync();
        }
        await using var reader = store.Open(store.TenantId);
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(11);
    }

    [Test]
    public async Task ImplicitPlanSaveAcquiresOriginalCustodyBeforeBeginWithoutDisclosureEffects()
    {
        await using var store = await Store.CreateAsync();
        var plan = NewWorkerPlan();
        var probe = new FanoutBeginProbe();
        await using (var writer = store.Open(store.TenantId, probe))
        {
            writer.Add(plan);

            await writer.SaveChangesAsync();

            await Assert.That(probe.ObservedPreBeginCustody).IsTrue();
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
            writer.RequireBoundTenantLease();
        }
        await using var reader = store.Open(store.TenantId);
        var persisted = await reader.TenantPlans.SingleAsync(row => row.Id == plan.Id);
        await Assert.That(persisted.Key).IsEqualTo(plan.Key);
        await Assert.That(persisted.DisplayName).IsEqualTo(plan.DisplayName);
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(11);
    }

    [Test]
    public async Task ExplicitPlanSaveRetainsOriginalCustodyAndItsOwnerRollsBackTheOnlyEffect()
    {
        await using var store = await Store.CreateAsync();
        var plan = NewWorkerPlan();
        var commands = new WorkerSourceDmlObserver(typeof(TenantPlan));
        await using (var writer = store.Open(store.TenantId, commands))
        {
            var connector = writer.Database.GetDbConnection();
            await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            writer.Add(plan);

            await writer.SaveChangesAsync();

            await Assert.That(commands.SourceCommands).IsEqualTo(1);
            await RequireOriginalOwnerAsync(writer, connector, transaction);
            EventDiscoveryEnrollmentGate.RequireCurrent(writer);
            await Assert.That(await writer.TenantPlans.AnyAsync(row => row.Id == plan.Id)).IsTrue();
            await transaction.RollbackAsync();
            EventDiscoveryEnrollmentGate.RequireCurrent(writer);
        }
        await using var reader = store.Open(store.TenantId);
        await Assert.That(await reader.TenantPlans.AnyAsync(row => row.Id == plan.Id)).IsFalse();
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(11);
    }

    private static TenantPlan NewWorkerPlan() => new()
    {
        Id = Guid.CreateVersion7(),
        Key = $"gate-{Guid.CreateVersion7():N}",
        DisplayName = "Worker source plan"
    };

    private static object WorkerSource(string kind) => kind switch
    {
        // These negative inputs must be refused before source validity or SQL is
        // considered. Empty credential fields are never persisted or admitted.
        "registration" => new ManagedControlPlaneRegistration
        {
            Id = Guid.CreateVersion7(),
            ManagedInstanceId = Guid.CreateVersion7(),
            EventInstanceId = Guid.CreateVersion7(),
            ControlPlaneEndpoint = "https://control.example.test",
            ManagementApiVersion = "1",
            EventVersion = "test",
            RequestHash = string.Empty,
            EventToControlPlaneKeyId = string.Empty,
            EventToControlPlaneSecretHash = string.Empty,
            ControlPlaneToEventKeyId = string.Empty,
            ControlPlaneToEventSecretHash = string.Empty,
            CredentialSecretBindingId = Guid.CreateVersion7()
        },
        "bootstrap" => InstanceBootstrapState.CreateInteractivePending(
            Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow),
        "plan" => NewWorkerPlan(),
        "version" => new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlanId = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            BillingPeriod = "monthly"
        },
        "setting" => new TenantPlanVersionSetting
        {
            Id = Guid.CreateVersion7(),
            TenantPlanVersionId = Guid.CreateVersion7(),
            SettingKey = "gate-setting",
            JsonValue = "true"
        },
        "quota" => new TenantPlanVersionQuota
        {
            Id = Guid.CreateVersion7(),
            TenantPlanVersionId = Guid.CreateVersion7(),
            QuotaKey = "gate-quota",
            Limit = 1
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class WorkerSourceDmlObserver(Type sourceType) : DbCommandInterceptor
    {
        internal int SourceCommands { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (eventData.Context is not ExploreDbContext context)
                return;
            var entity = context.Model.FindEntityType(sourceType)!;
            string table = context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (command.CommandText.Contains($"INSERT INTO {table}", StringComparison.Ordinal)
                || command.CommandText.Contains($"UPDATE {table}", StringComparison.Ordinal)
                || command.CommandText.Contains($"DELETE FROM {table}", StringComparison.Ordinal))
                SourceCommands++;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }
    }
}
