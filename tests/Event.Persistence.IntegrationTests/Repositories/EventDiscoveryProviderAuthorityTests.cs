using System.Data;
using System.Data.Common;
using System.Globalization;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Services.Scheduling;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class EventDiscoveryProviderAuthorityTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Test]
    [Arguments("a00", "Z01", false)]
    [Arguments("\u00e9", "\u00c9", false)]
    [Arguments("A", "A ", true)]
    [Arguments("\ud83d\ude00", "\ue000", false)]
    public async Task Native_title_seek_matches_invariant_uppercase_utf16_rank(
        string firstTitle, string secondTitle, bool reverseSourceIds)
    {
        var (lower, upper) = NewSourceOrderWitness();
        var scope = await SeedAsync(reverseSourceIds ? upper : lower, reverseSourceIds ? lower : upper,
            firstTitle, secondTitle);
        using var deadline = new CancellationTokenSource(Deadline);
        await using var context = scope.Open();
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var repository = new EventRepository(context);
            var specification = new EventQuerySpecification()
                .WithOccurrence(new(null, null, TemporalView.All, new DateTimeOffset(Now), null))
                .SortBy(EventSort.Title);
            var first = (await repository.SeekPublicDiscoveryAsync(specification, null, 1, token)).Single();
            await Assert.That(first.Id).IsEqualTo(scope.PrimaryEventId);
            var cursor = new EventDiscoverySourceCursor(first.Id, first.Title, first.TotalViews,
                first.CreatedAt, first.Sessions.Single().StartTime);
            var second = (await repository.SeekPublicDiscoveryAsync(specification, cursor, 1, token)).Single();
            await Assert.That(second.Id).IsEqualTo(scope.MemberEventId);
            return true;
        }, deadline.Token);
    }

    [Test]
    public async Task Native_equal_rank_ties_follow_canonical_source_guid_text_order()
    {
        var (lower, upper) = NewSourceOrderWitness();
        var scope = await SeedAsync(upper, lower);
        using var deadline = new CancellationTokenSource(Deadline);
        await using var context = scope.Open();
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var repository = new EventRepository(context);
            var specification = new EventQuerySpecification()
                .WithOccurrence(new(null, null, TemporalView.All, new DateTimeOffset(Now), null))
                .SortBy(EventSort.Title);
            var first = (await repository.SeekPublicDiscoveryAsync(specification, null, 1, token)).Single();
            await Assert.That(first.Id).IsEqualTo(scope.MemberEventId);
            var cursor = new EventDiscoverySourceCursor(first.Id, first.Title, first.TotalViews,
                first.CreatedAt, first.Sessions.Single().StartTime);
            var second = (await repository.SeekPublicDiscoveryAsync(specification, cursor, 1, token)).Single();
            await Assert.That(second.Id).IsEqualTo(scope.PrimaryEventId);
            return true;
        }, deadline.Token);
    }

    private static (Guid Lower, Guid Upper) NewSourceOrderWitness()
    {
        string suffix = Guid.CreateVersion7().ToString("D")[8..^1];
        // Canonical GUID text orders by the first group; SQL Server's native
        // uniqueidentifier order prioritizes the final bytes. Both IDs remain unique per case.
        return (Guid.Parse("00000000" + suffix + "1"), Guid.Parse("00000001" + suffix + "0"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_reservation_and_terminal_epoch_fences_exclude_an_independent_connection(bool terminalEpoch)
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using (var bootstrap = scope.Open())
            await new EfCoreUnitOfWork(bootstrap).ExecuteSerializableAsync(async ct =>
            {
                await new EventDiscoverySnapshotRepository(bootstrap).AcquireFenceAsync(scope.TenantId, ct);
                return true;
            }, token);

        Type rowType = terminalEpoch ? typeof(EventDiscoveryRevision) : typeof(EventDiscoverySnapshotReservation);
        await Assert.That(await NativeLockIsExcludedAsync(scope, rowType, token)).IsFalse();
        await using var owner = scope.Open();
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await owner.Database.OpenConnectionAsync(token);
            // SQLite's deferred BEGIN does not itself exclude the independent writer.
            // The production fence, rather than transaction creation or a managed semaphore, must do so.
            await using var native = owner.Database.IsSqlite()
                ? ((SqliteConnection)owner.Database.GetDbConnection()).BeginTransaction(IsolationLevel.Serializable, deferred: true)
                : null;
            await using var transaction = native is null
                ? await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
                : (await owner.Database.UseTransactionAsync(native, token))!;
            await Assert.That(await NativeLockIsExcludedAsync(scope, rowType, token)).IsFalse();
            if (terminalEpoch)
                await new EventDiscoveryDisclosureRepository(owner).AcquireCurrentAsync(scope.TenantId, token);
            else
                await new EventDiscoverySnapshotRepository(owner).AcquireFenceAsync(scope.TenantId, token);
            await Assert.That(await NativeLockIsExcludedAsync(scope, rowType, token)).IsTrue();
            await transaction.CommitAsync(token);
        });
        await Assert.That(await NativeLockIsExcludedAsync(scope, rowType, token)).IsFalse();
    }

    [Test]
    public async Task Subscribed_native_contenders_cannot_overbook_the_last_two_snapshot_slots()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var gate = new TransactionGate(3);
        var limits = new EventDiscoveryTraversalLimits(maxLiveSnapshots: 2, maxPhysicalItems: 20);
        Task<EventDiscoverySnapshot?>[] contenders = Enumerable.Range(1, 3).Select(index =>
            Task.Run(() => CaptureAsync(scope, Snapshot(scope, index), limits, Now, deadline.Token, gate),
                deadline.Token)).ToArray();
        EventDiscoverySnapshot?[] results = [];
        try
        {
            await gate.AllReached.WaitAsync(Deadline, deadline.Token);
        }
        finally
        {
            gate.Release();
            results = await Task.WhenAll(contenders).WaitAsync(Deadline, deadline.Token);
        }
        await Assert.That(results.Count(snapshot => snapshot is not null)).IsEqualTo(2);
        await Assert.That(results.Count(snapshot => snapshot is null)).IsEqualTo(1);
        await using var reader = scope.Open();
        var repository = new EventDiscoverySnapshotRepository(reader);
        foreach (var accepted in results.OfType<EventDiscoverySnapshot>())
        {
            await Assert.That(await repository.GetAsync(scope.TenantId, accepted.Id, deadline.Token)).IsNotNull();
            await Assert.That((await repository.GetItemsAsync(scope.TenantId, accepted.Id, -1, 100, deadline.Token)).Count)
                .IsEqualTo(1);
        }
        await Assert.That(await CaptureAsync(scope, Snapshot(scope, 4), limits, Now, deadline.Token)).IsNull();
    }

    [Test]
    public async Task Persisted_alias_review_invalidates_snapshot_reuse_without_changing_disclosure_epoch()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        var snapshot = await CaptureAsync(scope, Snapshot(scope, 1, count: 2), new(), Now, token)
            ?? throw new InvalidOperationException("Initial capture must fit its empty tenant budget.");
        await using (var reviewer = scope.Open())
            await new EfCoreUnitOfWork(reviewer).ExecuteSerializableAsync(async ct =>
            {
                var identities = new EventDiscoveryIdentityRepository(reviewer);
                await identities.AcquireFenceAsync(scope.TenantId, [scope.PrimaryIdentityId, scope.MemberIdentityId], ct);
                await identities.ReviewAsync(scope.TenantId, scope.MemberIdentityId, scope.PrimaryIdentityId,
                    scope.IdentityEpoch, scope.UserId, "same_event", Now, ct);
                return true;
            }, token);
        await using var reader = scope.Open();
        var identityRepository = new EventDiscoveryIdentityRepository(reader);
        var revision = await identityRepository.GetRevisionAsync(scope.TenantId, token)
            ?? throw new InvalidOperationException("The persisted revision must remain available.");
        await Assert.That(revision.IdentityEpoch).IsEqualTo(scope.IdentityEpoch + 1);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(scope.DisclosureEpoch);
        var group = await identityRepository.GetGroupAsync(scope.TenantId, scope.MemberIdentityId, token);
        await Assert.That(group.Count).IsEqualTo(2);
        await Assert.That(group.Single(identity => identity.Id == scope.MemberIdentityId).Alias!.PrimaryIdentityId)
            .IsEqualTo(scope.PrimaryIdentityId);
        var snapshots = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await snapshots.FindReusableAsync(scope.TenantId, Hash(1),
            revision.IdentityEpoch, revision.DisclosureEpoch, Now, token)).IsNull();
        await Assert.That((await snapshots.GetItemsAsync(scope.TenantId, snapshot.Id, -1, 100, token)).Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task Terminal_writer_advances_once_and_fresh_release_cannot_reuse_tracked_authority()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        var captured = await CaptureAsync(scope, Snapshot(scope, 1), new(), Now, token)
            ?? throw new InvalidOperationException("Initial capture must fit its empty tenant budget.");
        await using var reader = scope.Open();
        var tracked = await reader.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == scope.TenantId, token);
        var saved = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Task.Run(async () =>
        {
            await using var context = scope.Open();
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async ct =>
            {
                var tenant = await context.Tenants.SingleAsync(row => row.Id == scope.TenantId, ct);
                tenant.FullName = "First pending discovery change";
                await context.SaveChangesAsync(ct);
                tenant.FullName = "Committed discovery change";
                await context.SaveChangesAsync(ct);
                var pending = await new EventDiscoveryIdentityRepository(context).GetRevisionAsync(scope.TenantId, ct);
                saved.TrySetResult(pending!.DisclosureEpoch);
                await release.Task.WaitAsync(Deadline, ct);
                return true;
            }, token);
        }, token);
        try
        {
            await Assert.That(await saved.Task.WaitAsync(Deadline, token)).IsEqualTo(scope.DisclosureEpoch);
        }
        finally
        {
            release.TrySetResult();
            await writer.WaitAsync(Deadline, token);
        }
        var current = await new EfCoreUnitOfWork(reader).ExecuteReadCommittedAsync(
            ct => new EventDiscoveryDisclosureRepository(reader).AcquireCurrentAsync(scope.TenantId, ct), token);
        await Assert.That(tracked.DisclosureEpoch).IsEqualTo(scope.DisclosureEpoch);
        await Assert.That(current.DisclosureEpoch).IsEqualTo(scope.DisclosureEpoch + 1);
        await Assert.That(current.IdentityEpoch).IsEqualTo(scope.IdentityEpoch);
        var snapshots = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await snapshots.FindReusableAsync(scope.TenantId, Hash(1),
            current.IdentityEpoch, current.DisclosureEpoch, Now, token)).IsNull();
        await Assert.That(await snapshots.GetAsync(scope.TenantId, captured.Id, token)).IsNotNull();
        await Assert.That((await reader.Tenants.AsNoTracking()
            .SingleAsync(row => row.Id == scope.TenantId, token)).FullName).IsEqualTo("Committed discovery change");
        await using var other = scope.Open(scope.OtherTenantId);
        await Assert.That((await new EventDiscoveryIdentityRepository(other)
            .GetRevisionAsync(scope.OtherTenantId, token))!.DisclosureEpoch).IsEqualTo(scope.OtherDisclosureEpoch);
    }

    [Test]
    public async Task Source_mutation_after_terminal_release_rolls_back_without_invalidating_membership()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        var captured = await CaptureAsync(scope, Snapshot(scope, 1), new(), Now, token)
            ?? throw new InvalidOperationException("Initial capture must fit its empty tenant budget.");
        await using (var writer = scope.Open())
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
                {
                    await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(scope.TenantId, ct);
                    var tenant = await writer.Tenants.SingleAsync(row => row.Id == scope.TenantId, ct);
                    tenant.FullName = "Forbidden post-release write";
                    await writer.SaveChangesAsync(ct);
                    return true;
                }, token));
        await using var reader = scope.Open();
        var revision = await new EventDiscoveryIdentityRepository(reader).GetRevisionAsync(scope.TenantId, token);
        await Assert.That(revision!.DisclosureEpoch).IsEqualTo(scope.DisclosureEpoch);
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == scope.TenantId, token)).FullName)
            .IsEqualTo("Discovery provider tenant");
        var reusable = await new EventDiscoverySnapshotRepository(reader).FindReusableAsync(
            scope.TenantId, Hash(1), scope.IdentityEpoch, scope.DisclosureEpoch, Now, token);
        await Assert.That(reusable!.Id).IsEqualTo(captured.Id);
    }

    [Test]
    public async Task Current_release_never_returns_a_revision_older_than_a_completed_writer()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        bool historicalSnapshot = scope.Fixture.Provider is PrimaryDatabaseProvider.PostgreSql
            or PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb;
        await using var reader = scope.Open();
        await reader.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // PostgreSQL and InnoDB permit the committed writer alongside a historical
            // repeatable-read snapshot. SQL Server and SQLite use their fresh release
            // boundary; the independent native NOWAIT case proves their exclusion.
            await using var historical = historicalSnapshot
                ? await reader.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token)
                : null;
            long before = (await new EventDiscoveryIdentityRepository(reader)
                .GetRevisionAsync(scope.TenantId, token))!.DisclosureEpoch;
            await using (var writer = scope.Open())
                await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
                {
                    var tenant = await writer.Tenants.SingleAsync(row => row.Id == scope.TenantId, ct);
                    tenant.FullName = "Committed before final current read";
                    await writer.SaveChangesAsync(ct);
                    return true;
                }, token);
            if (historical is null)
            {
                var current = await new EfCoreUnitOfWork(reader).ExecuteReadCommittedAsync(
                    ct => new EventDiscoveryDisclosureRepository(reader).AcquireCurrentAsync(scope.TenantId, ct), token);
                await Assert.That(current.DisclosureEpoch).IsEqualTo(before + 1);
                return;
            }
            try
            {
                var current = await new EventDiscoveryDisclosureRepository(reader).AcquireCurrentAsync(scope.TenantId, token);
                await Assert.That(current.DisclosureEpoch).IsEqualTo(before + 1);
            }
            catch (PostgresException exception) when (
                scope.Fixture.Provider == PrimaryDatabaseProvider.PostgreSql && exception.SqlState == "40001")
            {
                // A stale PostgreSQL transaction must restart, never publish its old
                // revision. This is native serialization failure, not a simulated conflict.
                Console.WriteLine("PostgreSQL rejected the stale discovery snapshot with SQLSTATE 40001.");
            }
            finally
            {
                await historical.RollbackAsync(CancellationToken.None);
            }
        });
        await using var fresh = scope.Open();
        await Assert.That((await new EventDiscoveryIdentityRepository(fresh)
            .GetRevisionAsync(scope.TenantId, token))!.DisclosureEpoch).IsEqualTo(scope.DisclosureEpoch + 1);
    }

    [Test]
    public async Task Expiry_releases_logical_slots_but_only_bounded_purge_releases_physical_membership()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 2, maxLiveSnapshots: 1, maxPhysicalItems: 2);
        var first = await CaptureAsync(scope, Snapshot(scope, 1, count: 2), limits, Now, token)
            ?? throw new InvalidOperationException("Initial capture must fit its empty tenant budget.");
        var later = Now.AddMinutes(15);
        await using var reader = scope.Open();
        var repository = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await repository.FindReusableAsync(scope.TenantId, Hash(1),
            scope.IdentityEpoch, scope.DisclosureEpoch, later, token)).IsNull();
        await Assert.That((await repository.GetItemsAsync(scope.TenantId, first.Id, -1, 100, token)).Count).IsEqualTo(2);
        // The expired live slot is available, but both old physical rows still count.
        await Assert.That(await CaptureAsync(scope, Snapshot(scope, 2, created: later), limits, later, token)).IsNull();
        await new EfCoreUnitOfWork(reader).ExecuteSerializableAsync(async ct =>
        {
            await repository.AcquireFenceAsync(scope.TenantId, ct);
            await Assert.That(await repository.PurgeExpiredAsync(scope.TenantId, later, 1, ct)).IsEqualTo(1);
            return true;
        }, token);
        await Assert.That((await repository.GetItemsAsync(scope.TenantId, first.Id, -1, 100, token)).Count).IsEqualTo(0);
        var replacement = await CaptureAsync(scope, Snapshot(scope, 2, created: later), limits, later, token);
        await Assert.That(replacement).IsNotNull();
        var materialized = await repository.GetAsync(scope.TenantId, replacement!.Id, token);
        await Assert.That(materialized!.CreatedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(materialized.ExpiresAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task Expired_unpurged_snapshot_does_not_hold_a_live_slot_or_cross_tenant_boundaries()
    {
        var scope = await SeedAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        var limits = new EventDiscoveryTraversalLimits(maxLiveSnapshots: 1, maxPhysicalItems: 4);
        var first = await CaptureAsync(scope, Snapshot(scope, 1), limits, Now, token)
            ?? throw new InvalidOperationException("Initial capture must fit its empty tenant budget.");
        var otherSnapshot = EventDiscoverySnapshot.Create(Guid.CreateVersion7(), scope.OtherTenantId, Hash(1),
            0, scope.OtherDisclosureEpoch, Now, Now.AddMinutes(15), false, true, true,
            [EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, scope.OtherEventId,
                EventDiscoveryCanonicalKind.LocalEvent, scope.OtherEventId, scope.OtherSessionId)], limits);
        await using (var other = scope.Open(scope.OtherTenantId))
            await new EfCoreUnitOfWork(other).ExecuteSerializableAsync(async ct =>
            {
                var repository = new EventDiscoverySnapshotRepository(other);
                await repository.AcquireFenceAsync(scope.OtherTenantId, ct);
                await Assert.That(await repository.CaptureAsync(otherSnapshot, limits, Now, ct)).IsNotNull();
                return true;
            }, token);
        var later = Now.AddMinutes(15);
        var replacement = await CaptureAsync(scope, Snapshot(scope, 2, created: later), limits, later, token);
        await Assert.That(replacement).IsNotNull();
        await using var reader = scope.Open();
        var snapshots = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await snapshots.GetAsync(scope.TenantId, first.Id, token)).IsNotNull();
        await Assert.That(await snapshots.GetAsync(scope.OtherTenantId, otherSnapshot.Id, token)).IsNull();
        await Assert.That(await snapshots.GetAsync(scope.TenantId, otherSnapshot.Id, token)).IsNull();
        await Assert.That((await snapshots.GetItemsAsync(scope.OtherTenantId, otherSnapshot.Id, -1, 100, token)).Count)
            .IsEqualTo(0);
        await Assert.That(await new EventDiscoveryIdentityRepository(reader)
            .GetRevisionAsync(scope.OtherTenantId, token)).IsNull();
        await new EfCoreUnitOfWork(reader).ExecuteSerializableAsync(async ct =>
        {
            await snapshots.AcquireFenceAsync(scope.TenantId, ct);
            await Assert.That(await snapshots.PurgeExpiredAsync(scope.TenantId, later, 1, ct)).IsEqualTo(1);
            return true;
        }, token);
        await using var otherReader = scope.Open(scope.OtherTenantId);
        await Assert.That(await new EventDiscoverySnapshotRepository(otherReader)
            .GetAsync(scope.OtherTenantId, otherSnapshot.Id, token)).IsNotNull();
        await using var unscoped = scope.Fixture.CreateTenantContext(null);
        await Assert.That(await new EventDiscoverySnapshotRepository(unscoped)
            .GetAsync(scope.TenantId, replacement!.Id, token)).IsNull();
    }

    private static async Task<bool> NativeLockIsExcludedAsync(Scope scope, Type rowType, CancellationToken token)
    {
        await using var contender = scope.Open();
        await contender.Database.OpenConnectionAsync(token);
        var connection = contender.Database.GetDbConnection();
        if (connection is SqliteConnection sqlite)
        {
            int result = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "BEGIN IMMEDIATE");
            if (result == SQLitePCL.raw.SQLITE_OK)
            {
                await Assert.That(SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "ROLLBACK")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
                return false;
            }
            await Assert.That(result & 255).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
            return true;
        }

        var entity = contender.Model.FindEntityType(rowType)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var property = entity.FindProperty(nameof(EventDiscoveryRevision.TenantId))!;
        var sql = contender.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(store.Name, store.Schema);
        string key = sql.DelimitIdentifier(property.GetColumnName(store)!);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText = scope.Fixture.Provider == PrimaryDatabaseProvider.SqlServer
            ? $"SELECT {key} FROM {table} WITH (UPDLOCK, ROWLOCK, NOWAIT) WHERE {key} = @tenant"
            : $"SELECT {key} FROM {table} WHERE {key} = @tenant FOR UPDATE NOWAIT";
        command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, "@tenant", scope.TenantId));
        try
        {
            object? value = await command.ExecuteScalarAsync(token);
            if (value is null or DBNull)
                throw new InvalidOperationException("The native exclusion probe must target an existing authority row.");
            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == "55P03")
        {
            return true;
        }
        catch (SqlException exception) when (exception.Number == 1222)
        {
            return true;
        }
        catch (MySqlException exception) when (exception.Number is 3572 or 1205)
        {
            return true;
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<EventDiscoverySnapshot?> CaptureAsync(
        Scope scope, EventDiscoverySnapshot snapshot, EventDiscoveryTraversalLimits limits,
        DateTime now, CancellationToken token, params IInterceptor[] interceptors)
    {
        await using var context = scope.Fixture.CreateTenantContext(scope.TenantId, interceptors);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async ct =>
        {
            var repository = new EventDiscoverySnapshotRepository(context);
            await repository.AcquireFenceAsync(scope.TenantId, ct);
            var revision = await new EventDiscoveryDisclosureRepository(context).AcquireCurrentAsync(scope.TenantId, ct);
            if (revision.IdentityEpoch != snapshot.IdentityEpoch || revision.DisclosureEpoch != snapshot.DisclosureEpoch)
                throw new InvalidOperationException("Snapshot capture must use the current terminal epochs.");
            return await repository.CaptureAsync(snapshot, limits, now, ct);
        }, token);
    }

    private static EventDiscoverySnapshot Snapshot(Scope scope, int criteria, int count = 1, DateTime? created = null)
    {
        var time = created ?? Now;
        return EventDiscoverySnapshot.Create(Guid.CreateVersion7(), scope.TenantId, Hash(criteria),
            scope.IdentityEpoch, scope.DisclosureEpoch, time, time.AddMinutes(15), false, true, true,
            new[]
            {
                EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, scope.PrimaryEventId,
                    EventDiscoveryCanonicalKind.LocalEvent, scope.PrimaryEventId, scope.PrimarySessionId),
                EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, scope.MemberEventId,
                    EventDiscoveryCanonicalKind.LocalEvent, scope.MemberEventId, scope.MemberSessionId)
            }.Take(count), new());
    }

    private static string Hash(int value) => value.ToString("x64", CultureInfo.InvariantCulture);

    private static async Task<Scope> SeedAsync(
        Guid? primaryEventId = null, Guid? memberEventId = null,
        string primaryTitle = "Provider discovery event", string memberTitle = "Provider discovery event")
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await using (var probe = fixture.CreateSystemContext())
        {
            using var deadline = new CancellationTokenSource(Deadline);
            await probe.Database.OpenConnectionAsync(deadline.Token);
            string expected = fixture.Provider switch
            {
                PrimaryDatabaseProvider.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
                PrimaryDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
                PrimaryDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
                PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => "Microting.EntityFrameworkCore.MySql",
                _ => throw new InvalidOperationException("An explicit supported runtime provider is required.")
            };
            await Assert.That(probe.Database.ProviderName).IsEqualTo(expected);
            string version = probe.Database.GetDbConnection().ServerVersion;
            await Assert.That(string.IsNullOrWhiteSpace(version)).IsFalse();
            if (fixture.Provider is PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql)
                await Assert.That(version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
                    .IsEqualTo(fixture.Provider == PrimaryDatabaseProvider.MariaDb);
            Console.WriteLine($"Discovery authority provider: {fixture.Provider}; native server: {version}.");
        }
        await fixture.PrepareAsync();
        await using var seed = fixture.CreateSystemContext();
        var tenant = Tenant();
        var otherTenant = Tenant();
        var user = new User
        {
            Id = Guid.CreateVersion7(), CreatedAt = Now,
            Pii = new UserPii
            {
                Email = $"provider-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Provider", LastName = "Reviewer"
            }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = user.Id, User = user, Pii = new ActorPii { DisplayName = "Provider reviewer" }, CreatedAt = Now
        };
        seed.AddRange(tenant, otherTenant, user, actor);
        foreach (var owner in new[] { tenant, otherTenant })
        {
            seed.Set<EventDiscoveryRevision>().Add(new() { Id = Guid.CreateVersion7(), TenantId = owner.Id });
            seed.TenantUsers.Add(new()
            {
                Id = Guid.CreateVersion7(), TenantId = owner.Id, Tenant = owner,
                UserId = user.Id, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = Now
            });
        }
        var primary = AddEvent(tenant, primaryEventId, primaryTitle);
        var member = AddEvent(tenant, memberEventId, memberTitle);
        var other = AddEvent(otherTenant);
        var primaryIdentity = EventDiscoveryIdentity.Create(tenant.Id, EventDiscoverySourceKind.LocalEvent, primary.Event.ToString("D"));
        var memberIdentity = EventDiscoveryIdentity.Create(tenant.Id, EventDiscoverySourceKind.LocalEvent, member.Event.ToString("D"));
        seed.AddRange(primaryIdentity, memberIdentity);
        await seed.SaveChangesAsync();
        await using var current = fixture.CreateTenantContext(tenant.Id);
        var revision = await new EventDiscoveryIdentityRepository(current).GetRevisionAsync(tenant.Id, default)
            ?? throw new InvalidOperationException("Seeded discovery authority is unavailable.");
        await using var otherCurrent = fixture.CreateTenantContext(otherTenant.Id);
        var otherRevision = await new EventDiscoveryIdentityRepository(otherCurrent).GetRevisionAsync(otherTenant.Id, default)
            ?? throw new InvalidOperationException("Seeded alternate tenant authority is unavailable.");
        return new(fixture, tenant.Id, otherTenant.Id, user.Id, primary.Event, primary.Session,
            member.Event, member.Session, other.Event, other.Session, primaryIdentity.Id, memberIdentity.Id,
            revision.IdentityEpoch, revision.DisclosureEpoch, otherRevision.DisclosureEpoch);

        static Tenant Tenant() => new()
        {
            Id = Guid.CreateVersion7(), Slug = $"discovery-provider-{Guid.CreateVersion7():N}",
            FullName = "Discovery provider tenant", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };

        (Guid Event, Guid Session) AddEvent(
            Tenant owner, Guid? id = null, string title = "Provider discovery event")
        {
            var entity = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = id ?? Guid.CreateVersion7(), TenantId = owner.Id, Tenant = owner,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                Title = title, PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
                Timezone = "UTC", CreatedAt = Now
            };
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), TenantId = owner.Id, Tenant = owner,
                EventId = entity.Id, Event = entity, StartTime = new DateTimeOffset(Now),
                EndTime = new DateTimeOffset(Now.AddHours(1)), EndTimeType = SessionEndTimeType.Fixed
            };
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            seed.AddRange(entity, session);
            return (entity.Id, session.Id);
        }
    }

    private sealed record Scope(
        PrimaryDatabaseProviderBehaviorFixture Fixture, Guid TenantId, Guid OtherTenantId, Guid UserId,
        Guid PrimaryEventId, Guid PrimarySessionId, Guid MemberEventId, Guid MemberSessionId,
        Guid OtherEventId, Guid OtherSessionId, Guid PrimaryIdentityId, Guid MemberIdentityId,
        long IdentityEpoch, long DisclosureEpoch, long OtherDisclosureEpoch)
    {
        public ExploreDbContext Open(Guid? tenantId = null) => Fixture.CreateTenantContext(tenantId ?? TenantId);
    }

    private sealed class TransactionGate(int contenders) : DbTransactionInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AllReached => _arrived.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == contenders)
                _arrived.TrySetResult();
            await _release.Task.WaitAsync(Deadline, cancellationToken);
            return result;
        }
    }
}
