using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Federation;

[NotInParallel]
public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    public async Task ExplicitTenantCreationWithoutTheOriginalPreBeginGateRejectsBeforeTenantDml()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        var commands = new TenantDmlObserver();
        await using (var writer = store.Open(tenant.Id, commands))
        {
            var connector = writer.Database.GetDbConnection();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            writer.Tenants.Add(tenant);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());

            await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_gate_missing");
            await Assert.That(commands.TenantCommands).IsEqualTo(0);
            await RequireOriginalOwnerAsync(writer, connector, transaction);
            await transaction.RollbackAsync();
        }
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
    }

    [Test]
    public async Task ExplicitTenantDeletionWithoutTheOriginalPreBeginGateRejectsBeforeTenantDml()
    {
        await using var store = await Store.CreateAsync();
        var commands = new TenantDmlObserver();
        await using (var writer = store.Open(store.TenantId, commands))
        {
            var connector = writer.Database.GetDbConnection();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var tenant = await writer.Tenants.SingleAsync(row => row.Id == store.TenantId);
            var revision = await writer.Set<EventDiscoveryRevision>().SingleAsync();
            // Remove the real dependent row too, so a restrictive FK failure cannot
            // masquerade as the required pre-DML enrollment refusal.
            writer.Remove(revision);
            writer.Tenants.Remove(tenant);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());

            await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_gate_missing");
            await Assert.That(commands.TenantCommands).IsEqualTo(0);
            await RequireOriginalOwnerAsync(writer, connector, transaction);
            await transaction.RollbackAsync();
        }
        await using var reader = store.Open(store.TenantId);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == store.TenantId)).IsTrue();
        var retained = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(store.RevisionId);
        await Assert.That(retained.IdentityEpoch).IsEqualTo(7);
        await Assert.That(retained.DisclosureEpoch).IsEqualTo(11);
    }

    [Test]
    public async Task ReopenedIssuedConnectorCannotBecomeAnAbsentGateDuringTenantCreation()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        var commands = new TenantDmlObserver();
        await using var writer = store.Open(tenant.Id, commands);
        var connector = writer.Database.GetDbConnection();
        var lease = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        try
        {
            await writer.Database.CloseConnectionAsync();
            await writer.Database.OpenConnectionAsync();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            writer.Tenants.Add(tenant);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());

            await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_connector_changed");
            await Assert.That(commands.TenantCommands).IsEqualTo(0);
            await RequireOriginalOwnerAsync(writer, connector, transaction);
            await transaction.RollbackAsync();
        }
        finally
        {
            try
            {
                await lease.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
                // Lost custody still releases its own SQLite semaphore. Cleanup
                // must not close or otherwise claim the reopened connector.
            }
        }
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
        await using var next = await new EventDiscoveryEnrollmentGate(reader)
            .AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task GenuinePreBeginGatePreservesTheExplicitOwnerThroughSavedEffectsAndRollback()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        var commands = new TenantDmlObserver();
        await using var writer = store.Open(tenant.Id, commands);
        var connector = writer.Database.GetDbConnection();
        var lease = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        try
        {
            await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                writer.Tenants.Add(tenant);
                // This schema-capable SQLite owner supplies test data, not a new
                // Runtime/Global insertion capability. Nothing is committed.
                writer.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    IdentityEpoch = 3,
                    DisclosureEpoch = 5
                });
                await writer.SaveChangesAsync();

                await Assert.That(commands.TenantCommands > 0).IsTrue();
                await RequireOriginalOwnerAsync(writer, connector, transaction);
                EventDiscoveryEnrollmentGate.RequireCurrent(writer);
                await Assert.That(await writer.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsTrue();
                await Assert.That(await writer.Set<EventDiscoveryRevision>().CountAsync()).IsEqualTo(1);
                await transaction.RollbackAsync();
            }
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
            await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
            EventDiscoveryEnrollmentGate.RequireCurrent(writer);
            writer.RequireBoundTenantLease();
        }
        finally
        {
            await lease.DisposeAsync();
        }

        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
        await using var next = await new EventDiscoveryEnrollmentGate(reader)
            .AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task RequireOriginalOwnerAsync(
        ExploreDbContext context, DbConnection connector, IDbContextTransaction transaction)
    {
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(transaction.GetDbTransaction().Connection).IsSameReferenceAs(connector);
        context.RequireBoundTenantLease();
    }

    private static Tenant NewTenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Enrollment producer tenant",
        Slug = $"enrollment-producer-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class TenantDmlObserver : DbCommandInterceptor
    {
        internal int TenantCommands { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (eventData.Context is not ExploreDbContext context)
                return;
            var entity = context.Model.FindEntityType(typeof(Tenant))!;
            string table = context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (command.CommandText.Contains($"INSERT INTO {table}", StringComparison.Ordinal)
                || command.CommandText.Contains($"UPDATE {table}", StringComparison.Ordinal)
                || command.CommandText.Contains($"DELETE FROM {table}", StringComparison.Ordinal))
                TenantCommands++;
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

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"discovery-producer-{Guid.CreateVersion7():N}.db");
        internal Guid TenantId { get; } = Guid.CreateVersion7();
        internal Guid RevisionId { get; } = Guid.CreateVersion7();

        internal ExploreDbContext Open(Guid? tenantId, params IInterceptor[] interceptors)
        {
            var context = EmailDispatchSqliteFixture.CreateContext(path, interceptors);
            context.TenantContext = tenantId is { } selected ? new SelectedTenant(selected) : null;
            context.BindTenantLease();
            return context;
        }

        internal static async Task<Store> CreateAsync()
        {
            var store = new Store();
            try
            {
                await EmailDispatchSqliteFixture.CreateDatabaseAsync(store.path);
                await using var seed = store.Open(store.TenantId);
                await using var human = await new HumanSecuritySourceGate(seed).AcquireAsync();
                await using var gate = await new EventDiscoveryEnrollmentGate(seed).AcquireAsync();
                var tenant = NewTenant();
                tenant.Id = store.TenantId;
                seed.AddRange(tenant, new EventDiscoveryRevision
                {
                    Id = store.RevisionId,
                    TenantId = store.TenantId,
                    IdentityEpoch = 7,
                    DisclosureEpoch = 11
                });
                await seed.SaveChangesAsync();
                return store;
            }
            catch
            {
                await store.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete(path + "-shm");
            File.Delete(path + "-wal");
            return ValueTask.CompletedTask;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
