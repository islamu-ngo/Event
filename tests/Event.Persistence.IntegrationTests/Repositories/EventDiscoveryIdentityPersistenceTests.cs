using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryIdentityPersistenceTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Reviewer = Guid.CreateVersion7();
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Deleted_reviewer_cannot_retain_authority_through_an_active_membership()
    {
        await using var store = await Store.CreateAsync();
        var userId = Guid.CreateVersion7();
        await using (var seed = store.Open())
        {
            var user = new User
            {
                Id = userId, IsDeleted = true, Pii = new UserPii
                {
                    Email = $"deleted-{userId:N}@example.test", FirstName = "Deleted", LastName = "Subject"
                }
            };
            seed.Users.Add(user);
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = store.TenantId, Tenant = null!,
                UserId = userId, User = user, StatusId = (int)TenantUserStatusEnum.Active
            });
            await seed.SaveChangesAsync();
        }
        await using var context = store.Open();
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var membership = new TenantUserRepository(context);
            await Assert.That(await membership.FenceActiveTenantUserAsync(store.TenantId, userId, token)).IsFalse();
            return true;
        });
    }

    [Test]
    public async Task Bounded_binding_read_retains_aliases_and_tombstones_without_crossing_source_namespaces()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await DecideAsync(store, ids[1], ids[0], 0);
        await using (var writer = store.Open())
        {
            var suppressed = await writer.Set<EventDiscoveryIdentity>().SingleAsync(row => row.Id == ids[2]);
            suppressed.IsDeleted = true;
            await writer.SaveChangesAsync();
        }
        await using var reader = store.Open();
        var repository = new EventDiscoveryIdentityRepository(reader);
        var keys = ids.Select(id => store.SourceKeys[id]).ToArray();
        var bindings = await repository.GetBindingsAsync(
            store.TenantId, EventDiscoverySourceKind.LocalEvent, keys, CancellationToken.None);
        await Assert.That(bindings.Count).IsEqualTo(3);
        await Assert.That(bindings.Single(row => row.Id == ids[1]).Alias!.PrimaryIdentityId).IsEqualTo(ids[0]);
        await Assert.That(bindings.Single(row => row.Id == ids[2]).IsDeleted).IsTrue();
        await Assert.That((await repository.GetBindingsAsync(
            store.TenantId, EventDiscoverySourceKind.AtprotoRecord, keys, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That((await repository.GetBindingsAsync(
            store.OtherTenantId, EventDiscoverySourceKind.LocalEvent, keys, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.GetBindingsAsync(
            store.TenantId, EventDiscoverySourceKind.LocalEvent,
            Enumerable.Repeat(keys[0], 1001).ToArray(), CancellationToken.None));
    }

    [Test]
    public async Task Membership_fence_excludes_an_independent_native_revoker_until_commit()
    {
        await using var store = await Store.CreateAsync();
        var userId = Guid.CreateVersion7();
        await using (var seed = store.Open())
        {
            var user = new User
            {
                Id = userId, Pii = new UserPii
                {
                    Email = $"reviewer-{userId:N}@example.test", FirstName = "Review", LastName = "Subject"
                }
            };
            seed.Users.Add(user);
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = store.TenantId, Tenant = null!,
                UserId = userId, User = user, StatusId = (int)TenantUserStatusEnum.Active
            });
            await seed.SaveChangesAsync();
        }
        await using var review = store.Open();
        await using var revoke = store.Open();
        await revoke.Database.OpenConnectionAsync();
        await revoke.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 0");
        var entity = revoke.Model.FindEntityType(typeof(TenantUser))!;
        string tableName = entity.GetTableName()!;
        var tableMapping = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = revoke.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName, entity.GetSchema());
        string status = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantUser.StatusId))!
            .GetColumnName(tableMapping)!);
        var membership = new TenantUserRepository(review);
        await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await membership.FenceActiveTenantUserAsync(store.TenantId, userId, token)).IsTrue();
            // Zero busy timeout is a native NOWAIT probe, not a timing-based task completion check.
            // Bypass Microsoft.Data.Sqlite's managed busy retries for this exact native probe.
            int result = SQLitePCL.raw.sqlite3_exec(
                ((SqliteConnection)revoke.Database.GetDbConnection()).Handle,
                $"UPDATE {table} SET {status} = {status}");
            await Assert.That(result).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
            var subject = revoke.Model.FindEntityType(typeof(User))!;
            var subjectMapping = StoreObjectIdentifier.Table(subject.GetTableName()!, subject.GetSchema());
            string subjectTable = sql.DelimitIdentifier(subject.GetTableName()!, subject.GetSchema());
            string deleted = sql.DelimitIdentifier(subject.FindProperty(nameof(User.IsDeleted))!
                .GetColumnName(subjectMapping)!);
            int deletion = SQLitePCL.raw.sqlite3_exec(
                ((SqliteConnection)revoke.Database.GetDbConnection()).Handle,
                $"UPDATE {subjectTable} SET {deleted} = {deleted}");
            await Assert.That(deletion).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
            return true;
        });
        await revoke.TenantUsers.Where(row => row.UserId == userId).ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended));
        await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await membership.FenceActiveTenantUserAsync(store.TenantId, userId, token)).IsFalse();
            return true;
        });
    }

    [Test]
    public async Task Serializable_replay_discards_failed_identity_writes_and_reloads_authority()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        int attempts = 0;
        bool authorityAvailable = true;
        bool denied = false;
        try
        {
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync<bool>(async token =>
            {
                attempts++;
                await repository.AcquireFenceAsync(store.TenantId, ids, token);
                var revision = await repository.GetRevisionAsync(store.TenantId, token);
                await Assert.That(revision!.IdentityEpoch).IsEqualTo(0);
                if (!authorityAvailable)
                    throw new InvalidOperationException("authority_revoked");
                await repository.ReviewAsync(store.TenantId, ids[1], ids[0], 0,
                    Reviewer, "same_offering", Now, token);
                authorityAvailable = false;
                throw new SqliteException("Injected rollback-known writer conflict", 5);
            });
        }
        catch (InvalidOperationException exception) when (exception.Message == "authority_revoked")
        {
            denied = true;
        }
        await Assert.That(denied).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
        await using var reader = store.Open();
        var persisted = new EventDiscoveryIdentityRepository(reader);
        await Assert.That((await persisted.GetRevisionAsync(store.TenantId, CancellationToken.None))!.IdentityEpoch)
            .IsEqualTo(0);
        await Assert.That((await persisted.GetGroupAsync(store.TenantId, ids[1], CancellationToken.None)).Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Serializable_replay_is_bounded_and_does_not_retry_business_revision_conflicts()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Open();
        int attempts = 0;
        await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(async () =>
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync<bool>(_ =>
            {
                attempts++;
                throw new SqliteException("Injected rollback-known writer conflict", 5);
            }));
        await Assert.That(attempts).IsEqualTo(4);
        attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync<bool>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("discovery_revision_conflict");
            }));
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Reprimary_and_reversal_persist_direct_edges_without_moving_source_bindings()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await DecideAsync(store, ids[1], ids[0], 0);
        await DecideAsync(store, ids[2], ids[0], 1);
        await DecideAsync(store, ids[0], ids[1], 2);
        await using var reader = store.Open();
        var repository = new EventDiscoveryIdentityRepository(reader);
        var group = await repository.GetGroupAsync(store.TenantId, ids[2], CancellationToken.None);
        await Assert.That(group.Count).IsEqualTo(3);
        await Assert.That(group.Single(identity => identity.Id == ids[1]).Alias).IsNull();
        await Assert.That(group.Where(identity => identity.Id != ids[1])
            .All(identity => identity.Alias?.PrimaryIdentityId == ids[1])).IsTrue();
        await DecideAsync(store, ids[0], ids[1], 3, reverse: true);
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[0], CancellationToken.None)).Count)
            .IsEqualTo(1);
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[1], CancellationToken.None)).Count)
            .IsEqualTo(2);
        await Assert.That((await repository.GetRevisionAsync(store.TenantId, CancellationToken.None))!.IdentityEpoch)
            .IsEqualTo(4);
        foreach (var id in ids)
            await Assert.That((await repository.FindAsync(store.TenantId, EventDiscoverySourceKind.LocalEvent,
                store.SourceKeys[id], CancellationToken.None))!.Id).IsEqualTo(id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Competing_decision_loses_expected_revision_without_partial_graph(bool reversal)
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        if (reversal)
            await DecideAsync(store, ids[1], ids[0], 0);
        long expected = reversal ? 1 : 0;
        var winnerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWinner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenderGate = new TransactionStartGate();
        await using var winnerContext = store.Open();
        await using var contenderContext = store.Open(contenderGate);
        var winnerRepository = new EventDiscoveryIdentityRepository(winnerContext);
        var contenderRepository = new EventDiscoveryIdentityRepository(contenderContext);
        using var timeout = new CancellationTokenSource(Deadline);

        var winner = new EfCoreUnitOfWork(winnerContext).ExecuteSerializableAsync(async token =>
        {
            await winnerRepository.AcquireFenceAsync(store.TenantId, ids, token);
            if (reversal)
                await winnerRepository.ReverseAsync(store.TenantId, ids[1], ids[0], expected,
                    Reviewer, "different_offering", Now, token);
            else
                await winnerRepository.ReviewAsync(store.TenantId, ids[1], ids[0], expected,
                    Reviewer, "same_offering", Now, token);
            winnerReady.TrySetResult();
            await releaseWinner.Task.WaitAsync(Deadline, token);
            return true;
        }, timeout.Token);

        Task? contender = null;
        try
        {
            await winnerReady.Task.WaitAsync(Deadline);
            contender = Task.Run(() => new EfCoreUnitOfWork(contenderContext).ExecuteSerializableAsync(async token =>
            {
                await contenderRepository.AcquireFenceAsync(store.TenantId, ids.Reverse().ToArray(), token);
                await contenderRepository.ReviewAsync(store.TenantId, ids[1], ids[2], expected,
                    Reviewer, "same_offering", Now, token);
                return true;
            }, timeout.Token));
            await contenderGate.Reached.WaitAsync(Deadline);
            await Assert.That(contender.IsCompleted).IsFalse();
            contenderGate.Release();
            releaseWinner.TrySetResult();
            await winner.WaitAsync(Deadline);
            var rejection = await CaptureAsync(contender);
            await Assert.That(rejection).IsAssignableTo<InvalidOperationException>();
            await Assert.That(rejection!.Message).IsEqualTo("discovery_revision_conflict");
        }
        finally
        {
            contenderGate.Release();
            releaseWinner.TrySetResult();
            await winner.WaitAsync(Deadline);
            if (contender is not null)
                await CaptureAsync(contender);
        }

        await using var reader = store.Open();
        var repository = new EventDiscoveryIdentityRepository(reader);
        var group = await repository.GetGroupAsync(store.TenantId, ids[1], CancellationToken.None);
        await Assert.That(group.Count).IsEqualTo(reversal ? 1 : 2);
        await Assert.That(group.Single(identity => identity.Id == ids[1]).Alias?.PrimaryIdentityId)
            .IsEqualTo(reversal ? (Guid?)null : ids[0]);
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[2], CancellationToken.None)).Count)
            .IsEqualTo(1);
        await Assert.That((await repository.GetRevisionAsync(store.TenantId, CancellationToken.None))!.IdentityEpoch)
            .IsEqualTo(expected + 1);
    }

    [Test]
    public async Task Native_epoch_fence_blocks_a_connection_that_ignores_process_named_locks()
    {
        await using var store = await Store.CreateAsync();
        await store.SeedAsync();
        await using var owner = store.Open();
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await new EventDiscoveryIdentityRepository(owner).AcquireFenceAsync(store.TenantId, [], CancellationToken.None);
        var gate = new UpdateReached();
        await using var contender = store.Open(gate);
        contender.Database.SetCommandTimeout(1);
        var update = Task.Run(() => contender.Set<EventDiscoveryRevision>()
            .Where(revision => revision.TenantId == store.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(revision => revision.DisclosureEpoch, 99)));
        await gate.Reached.WaitAsync(Deadline);
        var exception = await CaptureAsync(update);
        await Assert.That(exception).IsAssignableTo<SqliteException>();
        await Assert.That(((SqliteException)exception!).SqliteErrorCode).IsEqualTo(5);
        await transaction.RollbackAsync();
        var affected = await contender.Set<EventDiscoveryRevision>()
            .Where(revision => revision.TenantId == store.TenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(revision => revision.DisclosureEpoch, 1));
        await Assert.That(affected).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_failure_rolls_back_saved_graph_and_epoch_and_allows_whole_operation_retry()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        await Assert.That(async () => await unitOfWork.ExecuteSerializableAsync<bool>(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, ids, token);
            await repository.ReviewAsync(store.TenantId, ids[1], ids[0], 0,
                Reviewer, "same_offering", Now, token);
            throw new InvalidOperationException("simulated_atomic_audit_failure");
        })).Throws<InvalidOperationException>();
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[1], CancellationToken.None)).Count)
            .IsEqualTo(1);
        await Assert.That((await repository.GetRevisionAsync(store.TenantId, CancellationToken.None))!.IdentityEpoch)
            .IsEqualTo(0);
        await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, ids, token);
            return await repository.ReviewAsync(store.TenantId, ids[1], ids[0], 0,
                Reviewer, "same_offering", Now, token);
        });
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[1], CancellationToken.None)).Count)
            .IsEqualTo(2);
    }

    [Test]
    public async Task Source_namespace_exact_key_and_retained_tombstone_prevent_resurrection()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        var key = Guid.CreateVersion7().ToString("D");
        var local = await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, [], token);
            var local = await repository.GetOrCreateAsync(store.TenantId, EventDiscoverySourceKind.LocalEvent, key, token);
            var remote = await repository.GetOrCreateAsync(store.TenantId, EventDiscoverySourceKind.AtprotoRecord, key, token);
            await Assert.That(remote.Id).IsNotEqualTo(local.Id);
            var upper = await repository.GetOrCreateAsync(store.TenantId, EventDiscoverySourceKind.AtprotoRecord,
                "at://did:plc:example/event/A", token);
            var lower = await repository.GetOrCreateAsync(store.TenantId, EventDiscoverySourceKind.AtprotoRecord,
                "at://did:plc:example/event/a", token);
            await Assert.That(upper.Id).IsNotEqualTo(lower.Id);
            return local;
        });
        local.IsDeleted = true;
        local.DeletedAt = Now;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await repository.FindAsync(store.TenantId, EventDiscoverySourceKind.LocalEvent, key,
            CancellationToken.None)).IsNull();
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, [local.Id], token);
            return await repository.GetOrCreateAsync(store.TenantId, EventDiscoverySourceKind.LocalEvent, key, token);
        })).Throws<InvalidOperationException>();
        await Assert.That(await repository.FindAsync(store.TenantId, EventDiscoverySourceKind.AtprotoRecord, key,
            CancellationToken.None)).IsNotNull();
    }

    [Test]
    public async Task Tenant_substitution_missing_context_bypass_and_soft_delete_fail_closed()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        await Assert.That((await repository.GetGroupAsync(store.OtherTenantId, ids[0], CancellationToken.None)).Count)
            .IsEqualTo(0);
        context.TenantContext = null;
        await Assert.That(await repository.GetRevisionAsync(store.TenantId, CancellationToken.None)).IsNull();
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[0], CancellationToken.None)).Count)
            .IsEqualTo(0);
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, ids, token);
            return true;
        })).Throws<InvalidOperationException>();
        context.TenantContext = new TenantScope(store.TenantId);
        context.EnableTenantFilterBypass("Identity test rejects unscoped bypass.");
        await Assert.That(await repository.FindAsync(store.TenantId, EventDiscoverySourceKind.LocalEvent,
            store.SourceKeys[ids[0]], CancellationToken.None)).IsNull();
        context.ClearTenantFilterBypass();
        var deleted = await context.Set<EventDiscoveryIdentity>().SingleAsync(identity => identity.Id == ids[0]);
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[0], CancellationToken.None)).Count)
            .IsEqualTo(0);
    }

    [Test]
    [Arguments("self")]
    [Arguments("cross_tenant")]
    [Arguments("revision")]
    [Arguments("source_duplicate")]
    public async Task Database_rejects_malformed_rows_without_a_partial_relationship(string violation)
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await using var context = store.Open();
        if (violation == "source_duplicate")
            context.Set<EventDiscoveryIdentity>().Add(EventDiscoveryIdentity.Create(store.TenantId,
                EventDiscoverySourceKind.LocalEvent, store.SourceKeys[ids[0]]));
        else
            context.Set<EventDiscoveryAlias>().Add(new EventDiscoveryAlias
            {
                Id = Guid.CreateVersion7(), TenantId = violation == "cross_tenant" ? store.OtherTenantId : store.TenantId,
                MemberIdentityId = ids[1], PrimaryIdentityId = violation == "self" ? ids[1] : ids[0],
                RelationshipRevision = violation == "revision" ? 0 : 1, ReviewerId = Reviewer,
                ReasonCode = "same_offering", ReviewedAtUtc = Now
            });
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();
        context.ChangeTracker.Clear();
        await Assert.That((await new EventDiscoveryIdentityRepository(context)
            .GetGroupAsync(store.TenantId, ids[1], CancellationToken.None)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Disclosure_and_identity_epochs_share_one_transaction_but_advance_independently()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        await Assert.That(async () => await repository.AdvanceDisclosureAsync(store.TenantId, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, ids, token);
            await repository.ReviewAsync(store.TenantId, ids[1], ids[0], 0, Reviewer, "same_offering", Now, token);
            return await repository.AdvanceDisclosureAsync(store.TenantId, token);
        });
        var revision = await repository.GetRevisionAsync(store.TenantId, CancellationToken.None);
        await Assert.That(revision!.IdentityEpoch).IsEqualTo(1);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(1);
    }

    [Test]
    public async Task Mutating_the_callers_fence_list_cannot_authorize_an_unfenced_identity()
    {
        await using var store = await Store.CreateAsync();
        var ids = await store.SeedAsync();
        var gate = new UpdateReached(pause: true);
        await using var context = store.Open(gate);
        var repository = new EventDiscoveryIdentityRepository(context);
        var requested = new List<Guid> { ids[1], ids[0] };
        var operation = new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, requested, token);
            return await repository.ReviewAsync(store.TenantId, ids[2], ids[0], 0,
                Reviewer, "same_offering", Now, token);
        });
        try
        {
            await gate.Reached.WaitAsync(Deadline);
            requested.Add(ids[2]);
        }
        finally
        {
            gate.Release();
        }
        await Assert.That(await CaptureAsync(operation)).IsAssignableTo<InvalidOperationException>();
        await Assert.That((await repository.GetRevisionAsync(store.TenantId, CancellationToken.None))!.IdentityEpoch)
            .IsEqualTo(0);
        await Assert.That((await repository.GetGroupAsync(store.TenantId, ids[2], CancellationToken.None)).Count)
            .IsEqualTo(1);
    }

    private static async Task DecideAsync(Store store, Guid member, Guid primary, long expected, bool reverse = false)
    {
        await using var context = store.Open();
        var repository = new EventDiscoveryIdentityRepository(context);
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await repository.AcquireFenceAsync(store.TenantId, [member, primary], token);
            return reverse
                ? await repository.ReverseAsync(store.TenantId, member, primary, expected,
                    Reviewer, "different_offering", Now, token)
                : await repository.ReviewAsync(store.TenantId, member, primary, expected,
                    Reviewer, "same_offering", Now, token);
        });
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(Deadline);
            return null;
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            return exception;
        }
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"discovery-identity-{Guid.CreateVersion7():N}.db");
        private DbContextOptions<ExploreDbContext> _options;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();
        public Dictionary<Guid, string> SourceKeys { get; } = [];

        private Store()
        {
            _options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = _path, DefaultTimeout = 5, Pooling = false
                }.ToString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                    SqliteProjectionLockTransactionInterceptor.Instance)
                .Options;
        }

        public ExploreDbContext Open(params IInterceptor[] interceptors)
        {
            var context = new ExploreDbContext(TestDbContextOptions.Create(_options)
                .AddInterceptors(interceptors).Options);
            context.TenantContext = new TenantScope(TenantId);
            return context;
        }

        public static async Task<Store> CreateAsync()
        {
            var store = new Store();
            await using var context = store.Open();
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            // Reuse immutable schema, not contexts/connections or scoped tenant state. Model construction
            // must not consume a contender's bounded race deadline.
            store._options = TestDbContextOptions.Create(store._options).UseModel(context.Model).Options;
            context.Set<TenantStatus>().Add(new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true
            });
            foreach (var tenantId in new[] { store.TenantId, store.OtherTenantId })
                context.Tenants.Add(new Tenant
                {
                    Id = tenantId, Slug = $"identity-{tenantId:N}", FullName = "Identity tenant",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                });
            await context.SaveChangesAsync();
            return store;
        }

        public async Task<Guid[]> SeedAsync()
        {
            await using var context = Open();
            var identities = Enumerable.Range(0, 3).Select(_ =>
            {
                var sourceKey = Guid.CreateVersion7().ToString("D");
                var identity = EventDiscoveryIdentity.Create(TenantId, EventDiscoverySourceKind.LocalEvent, sourceKey);
                SourceKeys.Add(identity.Id, sourceKey);
                return identity;
            }).ToArray();
            context.Set<EventDiscoveryIdentity>().AddRange(identities);
            context.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId
            });
            await context.SaveChangesAsync();
            return identities.Select(identity => identity.Id).ToArray();
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-shm");
            File.Delete(_path + "-wal");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TransactionStartGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Reached => _reached.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            _reached.TrySetResult();
            await _release.Task.WaitAsync(Deadline, cancellationToken);
            return result;
        }
    }

    private sealed class UpdateReached(bool pause = false) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Reached => _reached.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _reached.TrySetResult();
            if (pause)
                await _release.Task.WaitAsync(Deadline, cancellationToken);
            return result;
        }
    }
}
