using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoverySnapshotPersistenceTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Test]
    public async Task Reservation_does_not_write_public_tenant_authority_or_reference_it_after_the_epoch()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Open();
        string tenantTable = context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(context.Model.FindEntityType(typeof(Tenant))!.GetTableName()!);
        string sourceWriteGuard = $"CREATE TRIGGER reject_discovery_tenant_write BEFORE UPDATE ON {tenantTable} " +
            "BEGIN SELECT RAISE(ABORT, 'public_source_write'); END;";
        await context.Database.ExecuteSqlRawAsync(sourceWriteGuard);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new EventDiscoverySnapshotRepository(context);
        await repository.AcquireFenceAsync(store.TenantId, default);
        await Assert.That(await repository.CaptureAsync(Snapshot(store.TenantId, 1), new(), Now, default)).IsNotNull();
        // Native FK checks run during snapshot insertion, after the terminal epoch fence.
        // They must not introduce a new dependency back to the public Tenant source row.
        await Assert.That(context.Model.FindEntityType(typeof(EventDiscoverySnapshot))!.GetForeignKeys()
            .Any(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Tenant))).IsFalse();
        await Assert.That(context.Model.FindEntityType(typeof(EventDiscoverySnapshotReservation))!.GetForeignKeys()
            .Any()).IsFalse();
        await transaction.CommitAsync();
    }

    [Test]
    public async Task Two_hundred_and_one_subscribed_contenders_cannot_allocate_a_two_hundred_and_first_live_slot()
    {
        await using var store = await Store.CreateAsync();
        var gate = new TransactionGate(201);
        var operations = Enumerable.Range(0, 201).Select(index =>
            CaptureAsync(store, Snapshot(store.TenantId, index), new(), Now, gate)).ToArray();
        try
        {
            await gate.AllReached.WaitAsync(Deadline);
        }
        finally
        {
            gate.Release();
        }
        var results = await Task.WhenAll(operations).WaitAsync(Deadline);
        await Assert.That(results.Count(result => result is not null)).IsEqualTo(200);
        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await using var reader = store.Open();
        await Assert.That(await reader.EventDiscoverySnapshotReservations.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Reuse_is_tenant_criteria_epoch_and_expiry_exact_without_allocating_another_slot()
    {
        await using var store = await Store.CreateAsync();
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 2, maxLiveSnapshots: 1, maxPhysicalItems: 4);
        var first = await CaptureAsync(store, Snapshot(store.TenantId, 1), limits, Now);
        var same = await CaptureAsync(store, Snapshot(store.TenantId, 1), limits, Now.AddMinutes(1));
        await Assert.That(same!.Id).IsEqualTo(first!.Id);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 2), limits, Now)).IsNull();
        await using var reader = store.Open();
        var repository = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await repository.FindReusableAsync(store.TenantId, Hash(1), 1, 0, Now, default)).IsNull();
        await Assert.That(await repository.FindReusableAsync(store.TenantId, Hash(1), 0, 1, Now, default)).IsNull();
        await Assert.That(await repository.FindReusableAsync(store.OtherTenantId, Hash(1), 0, 0, Now, default)).IsNull();
        await Assert.That(await repository.FindReusableAsync(
            store.TenantId, Hash(1), 0, 0, Now.AddMinutes(15), default)).IsNull();
        var replacement = await CaptureAsync(store,
            Snapshot(store.TenantId, 2, created: Now.AddMinutes(15)), limits, Now.AddMinutes(15));
        await Assert.That(replacement).IsNotNull();
        await Assert.That(replacement!.Id).IsNotEqualTo(first.Id);
        // No purge ran: logical expiry released the slot without deleting the old membership.
        await Assert.That(await repository.GetAsync(store.TenantId, first.Id, default)).IsNotNull();
    }

    [Test]
    public async Task Physical_saturation_counts_actual_rows_even_when_expired_metadata_understates_membership()
    {
        await using var store = await Store.CreateAsync();
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 2, maxLiveSnapshots: 1, maxPhysicalItems: 2);
        var first = await CaptureAsync(store, Snapshot(store.TenantId, 1, count: 2), limits, Now);
        await using (var corruptMetadata = store.Open())
        {
            await corruptMetadata.Set<EventDiscoverySnapshot>().Where(snapshot => snapshot.Id == first!.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(snapshot => snapshot.ItemCount, 0));
        }
        var later = Now.AddMinutes(15);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 2, created: later), limits, later))
            .IsNull();
        await using var reader = store.Open();
        var repository = new EventDiscoverySnapshotRepository(reader);
        await Assert.That((await repository.GetItemsAsync(store.TenantId, first!.Id, -1, 100, default)).Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task Native_tenant_fence_blocks_an_independent_connection_without_a_process_lock()
    {
        await using var store = await Store.CreateAsync();
        await using var holder = store.Open();
        await using var contender = store.Open();
        await contender.Database.OpenConnectionAsync();
        await contender.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 0");
        var connection = (SqliteConnection)contender.Database.GetDbConnection();
        await holder.Database.OpenConnectionAsync();
        await using (var native = ((SqliteConnection)holder.Database.GetDbConnection())
                         .BeginTransaction(IsolationLevel.Serializable, deferred: true))
        {
            await using var transaction = await holder.Database.UseTransactionAsync(native);
            // A deferred transaction alone does not exclude this writer: prove the fence makes the difference.
            await Assert.That(SQLitePCL.raw.sqlite3_exec(connection.Handle, "BEGIN IMMEDIATE"))
                .IsEqualTo(SQLitePCL.raw.SQLITE_OK);
            await Assert.That(SQLitePCL.raw.sqlite3_exec(connection.Handle, "ROLLBACK"))
                .IsEqualTo(SQLitePCL.raw.SQLITE_OK);
            await new EventDiscoverySnapshotRepository(holder).AcquireFenceAsync(store.TenantId, default);
            int result = SQLitePCL.raw.sqlite3_exec(connection.Handle, "BEGIN IMMEDIATE");
            await Assert.That(result).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
            await native.CommitAsync();
        }
        await Assert.That(SQLitePCL.raw.sqlite3_exec(connection.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
        await Assert.That(SQLitePCL.raw.sqlite3_exec(connection.Handle, "ROLLBACK")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
    }

    [Test]
    public async Task Membership_keyset_and_purge_are_bounded_and_cannot_cross_tenants()
    {
        await using var store = await Store.CreateAsync();
        var one = await CaptureAsync(store, Snapshot(store.TenantId, 1, count: 3), new(), Now);
        var two = await CaptureAsync(store, Snapshot(store.TenantId, 2), new(), Now);
        await using (var other = store.Open(store.OtherTenantId))
        {
            var repository = new EventDiscoverySnapshotRepository(other);
            await using var transaction = await other.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await repository.AcquireFenceAsync(store.OtherTenantId, default);
            await repository.CaptureAsync(Snapshot(store.OtherTenantId, 1), new(), Now, default);
            await transaction.CommitAsync();
        }
        await using var context = store.Open();
        var reader = new EventDiscoverySnapshotRepository(context);
        var page = await reader.GetItemsAsync(store.TenantId, one!.Id, 0, 1, default);
        await Assert.That(page.Count).IsEqualTo(1);
        await Assert.That(page[0].Ordinal).IsEqualTo(1L);
        await Assert.That((await reader.GetItemsAsync(store.OtherTenantId, one.Id, -1, 100, default)).Count)
            .IsEqualTo(0);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await reader.GetItemsAsync(store.TenantId, one.Id, -1, 101, default));
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await reader.AcquireFenceAsync(store.TenantId, default);
            await Assert.That(await reader.PurgeExpiredAsync(store.TenantId, Now.AddMinutes(15), 1, default))
                .IsEqualTo(1);
            await transaction.CommitAsync();
        }
        var remaining = new[]
        {
            await reader.GetAsync(store.TenantId, one.Id, default),
            await reader.GetAsync(store.TenantId, two!.Id, default)
        };
        await Assert.That(remaining.Count(snapshot => snapshot is not null)).IsEqualTo(1);
        await using var otherReader = store.Open(store.OtherTenantId);
        await Assert.That(await new EventDiscoverySnapshotRepository(otherReader)
            .FindReusableAsync(store.OtherTenantId, Hash(1), 0, 0, Now, default)).IsNotNull();
        await Assert.That(await otherReader.Set<EventDiscoverySnapshot>().CountAsync()).IsEqualTo(1);
        await Assert.That(await otherReader.Set<EventDiscoverySnapshotItem>().CountAsync()).IsEqualTo(1);
        await Assert.That(await otherReader.EventDiscoverySnapshotReservations.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Empty_searches_cannot_accumulate_unbounded_headers_when_purge_stops()
    {
        await using var store = await Store.CreateAsync();
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 1, maxLiveSnapshots: 1, maxPhysicalItems: 2);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 1, count: 0), limits, Now)).IsNotNull();
        var later = Now.AddMinutes(15);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 2, count: 0, created: later), limits, later))
            .IsNotNull();
        later = later.AddMinutes(15);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 3, count: 0, created: later), limits, later))
            .IsNull();
    }

    [Test]
    public async Task Purge_releases_actual_member_capacity_and_materialized_timestamps_remain_utc()
    {
        await using var store = await Store.CreateAsync();
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 2, maxLiveSnapshots: 1, maxPhysicalItems: 2);
        var first = await CaptureAsync(store, Snapshot(store.TenantId, 1, count: 2), limits, Now);
        await using var context = store.Open();
        var repository = new EventDiscoverySnapshotRepository(context);
        var stored = await repository.GetAsync(store.TenantId, first!.Id, default);
        await Assert.That(stored!.CreatedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(stored.ExpiresAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        var later = Now.AddMinutes(15);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await repository.AcquireFenceAsync(store.TenantId, default);
            await Assert.That(await repository.PurgeExpiredAsync(store.TenantId, later, 1, default)).IsEqualTo(1);
            await transaction.CommitAsync();
        }
        await Assert.That((await repository.GetItemsAsync(store.TenantId, first.Id, -1, 100, default)).Count).IsEqualTo(0);
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 2, 2, later), limits, later)).IsNotNull();
    }

    [Test]
    public async Task Reservation_requires_active_serializable_fence_and_rollback_releases_capacity()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Open();
        var repository = new EventDiscoverySnapshotRepository(context);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.AcquireFenceAsync(store.TenantId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.CaptureAsync(Snapshot(store.TenantId, 1), new(), Now, default));
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await repository.AcquireFenceAsync(store.OtherTenantId, default));
            await repository.AcquireFenceAsync(store.TenantId, default);
            await repository.CaptureAsync(Snapshot(store.TenantId, 1),
                new(maxLiveSnapshots: 1), Now, default);
            await transaction.RollbackAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.CaptureAsync(Snapshot(store.TenantId, 2), new(), Now, default));
        await Assert.That(await CaptureAsync(store, Snapshot(store.TenantId, 2), new(maxLiveSnapshots: 1), Now))
            .IsNotNull();
    }

    private static string Hash(int value) => value.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static EventDiscoverySnapshot Snapshot(Guid tenantId, int criteria, int count = 1, DateTime? created = null)
    {
        var time = created ?? Now;
        return EventDiscoverySnapshot.Create(Guid.CreateVersion7(), tenantId, Hash(criteria), 0, 0, time, time.AddMinutes(15),
            false, true, true, Enumerable.Range(0, count).Select(_ =>
                EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7(),
                    EventDiscoveryCanonicalKind.LocalEvent, Guid.CreateVersion7(), Guid.CreateVersion7())), new());
    }

    private static async Task<EventDiscoverySnapshot?> CaptureAsync(
        Store store, EventDiscoverySnapshot snapshot, EventDiscoveryTraversalLimits limits,
        DateTime now, params IInterceptor[] interceptors)
    {
        await using var context = store.Open(interceptors: interceptors);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new EventDiscoverySnapshotRepository(context);
        await repository.AcquireFenceAsync(store.TenantId, default);
        var result = await repository.CaptureAsync(snapshot, limits, now, default);
        await transaction.CommitAsync();
        return result;
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    internal sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"discovery-snapshot-{Guid.CreateVersion7():N}.db");
        private DbContextOptions<ExploreDbContext> _options;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();

        private Store()
        {
            _options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = _path, DefaultTimeout = 30, Pooling = false
                }.ToString())
                .UseSnakeCaseNamingConvention().Options;
        }

        public ExploreDbContext Open(Guid? tenantId = null, params IInterceptor[] interceptors)
        {
            var context = new ExploreDbContext(TestDbContextOptions.Create(_options)
                .AddInterceptors(interceptors).Options);
            context.TenantContext = new TenantScope(tenantId ?? TenantId);
            return context;
        }

        public static async Task<Store> CreateAsync()
        {
            var store = new Store();
            await using var context = store.Open();
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, default);
            store._options = TestDbContextOptions.Create(store._options).UseModel(context.Model).Options;
            context.Set<TenantStatus>().Add(new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true
            });
            foreach (var tenant in new[] { store.TenantId, store.OtherTenantId })
                context.Tenants.Add(new Tenant
                {
                    Id = tenant, Slug = $"snapshot-{tenant:N}", FullName = "Snapshot tenant",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                });
            await context.SaveChangesAsync();
            return store;
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-shm");
            File.Delete(_path + "-wal");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TransactionGate(int contenders) : DbTransactionInterceptor
    {
        private int _reached;
        private readonly TaskCompletionSource _allReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AllReached => _allReached.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reached) == contenders)
                _allReached.TrySetResult();
            await _release.Task.WaitAsync(Deadline, cancellationToken);
            return result;
        }
    }
}
