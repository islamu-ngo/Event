using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests.Discovery;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresDiscoveryTraversalTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task SameTenantDisclosureSettlementRetainsTheOriginalSelectedCapability(PrimaryDatabaseRole role)
    {
        Guid tenant = await InstallTenantAsync();
        long before;
        await using (var observation = fixture.CreateDbContext())
            before = await observation.Set<EventDiscoveryRevision>()
                .Where(value => value.TenantId == tenant)
                .Select(value => (long?)value.DisclosureEpoch).SingleOrDefaultAsync() ?? 0;
        await using var owner = Open(role, tenant);
        owner.BindTenantLease();
        await using (var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await new EventDiscoveryDisclosureRepository(owner).AdvanceAsync([tenant], default);
            await transaction.CommitAsync();
        }
        await using var verification = fixture.CreateDbContext();
        var revision = await verification.Set<EventDiscoveryRevision>()
            .SingleAsync(value => value.TenantId == tenant);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(before + 1);
    }

    [Test]
    public async Task GlobalWithoutDisclosureCapabilityCannotBorrowTraversalForSettlement()
    {
        Guid tenant = await InstallTenantAsync();
        await using var owner = Open(PrimaryDatabaseRole.Global, tenant);
        owner.BindTenantLease();
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            new EventDiscoveryDisclosureRepository(owner).AdvanceAsync([tenant], default));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task OrdinaryRuntimeCapturesReferencesAndReadsThemWithoutPrivateTableAccess()
    {
        Guid tenant = await InstallTenantAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        await runtime.Database.OpenConnectionAsync();
        await PostgresDiscoveryTraversalContract.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        var snapshots = new EventDiscoverySnapshotRepository(runtime);
        var disclosure = new EventDiscoveryDisclosureRepository(runtime);
        Guid source = Guid.CreateVersion7();
        EventDiscoverySnapshot captured;
        await using (var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await snapshots.AcquireFenceAsync(tenant, default);
            var revision = await disclosure.AcquireCurrentAsync(tenant, default);
            var now = await NowAsync(runtime);
            captured = Snapshot(tenant, revision, now, source);
            await Assert.That(await snapshots.CaptureAsync(captured, new(), now, default)).IsNotNull();
            await transaction.CommitAsync();
        }
        await Assert.That((await snapshots.GetAsync(tenant, captured.Id, default))!.ItemCount).IsEqualTo(1);
        var membership = await snapshots.GetItemsAsync(tenant, captured.Id, -1, 100, default);
        await Assert.That(membership.Single().SourceId).IsEqualTo(source);
        await Assert.That(membership.Single().CanonicalId).IsEqualTo(source);
        await Assert.That(membership.Single().Ordinal).IsEqualTo(0);
        await Assert.That((await new EventDiscoveryIdentityRepository(runtime).GetRevisionAsync(tenant, default))!.Id)
            .IsNotEqualTo(Guid.Empty);
        foreach (var type in new[] { typeof(Tenant), typeof(EventDiscoveryRevision), typeof(EventDiscoverySnapshot),
                     typeof(EventDiscoverySnapshotItem), typeof(EventDiscoverySnapshotReservation),
                     typeof(EventDiscoveryIdentity), typeof(EventDiscoveryAlias) })
        {
            string table = new HumanPrincipalSourceSql(runtime).Table(type);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                runtime.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 1"));
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    public async Task ForeignTenantAndMissingTenantCannotUseNativeOperations()
    {
        Guid tenant = await InstallTenantAsync();
        foreach (Guid? selected in new Guid?[] { tenant, null })
        {
            await using var runtime = Open(PrimaryDatabaseRole.Runtime, selected);
            await runtime.Database.OpenConnectionAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                NativeAsync(runtime, "revision_read", selected.HasValue ? Guid.CreateVersion7() : tenant));
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task OtherSelectedPurposesCannotBorrowOrdinaryTraversal(PrimaryDatabaseRole role)
    {
        Guid tenant = await InstallTenantAsync();
        await using var caller = Open(role, tenant);
        await caller.Database.OpenConnectionAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => NativeAsync(caller, "revision_read", tenant));
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RuntimeCannotAssumeTraversalOwnerOrDisclosureAuthority()
    {
        Guid tenant = await InstallTenantAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        await runtime.Database.OpenConnectionAsync();
        foreach (string role in new[] { PostgresDiscoveryTraversalContract.OwnerRole,
                     PostgresDiscoveryTraversalContract.RuntimeRole, PostgresDiscoveryDisclosureContract.RuntimeRole })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                runtime.Database.ExecuteSqlRawAsync($"SET ROLE \"{role}\""));
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    public async Task NativePagingAndPurgeRejectUnboundedOrUnfencedRequests()
    {
        Guid tenant = await InstallTenantAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        await runtime.Database.OpenConnectionAsync();
        var sql = new HumanPrincipalSourceSql(runtime);
        await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {sql.Name("discovery_traversal_snapshot_items")}({{0}},{{1}},-1,101)", tenant, Guid.CreateVersion7()));
        await using var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync(
            $"SELECT {sql.Name("discovery_traversal_snapshot_purge")}({{0}},statement_timestamp(),1)", tenant));
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ReadinessAndReinstallationRejectChangedRoutineRatherThanRepairingIt()
    {
        await InstallTenantAsync();
        foreach (bool reinstall in new[] { false, true })
        {
            await using var migration = fixture.CreateDbContext();
            await migration.Database.OpenConnectionAsync();
            var sql = new HumanPrincipalSourceSql(migration);
            await migration.Database.ExecuteSqlRawAsync(
                $"ALTER FUNCTION {sql.Name("discovery_traversal_revision_read")}(uuid) IMMUTABLE");
            try
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() => reinstall
                    ? PostgresDiscoveryTraversalContract.ApplyAsync(migration, fixture.NativeQueueEnrollment)
                    : PostgresDiscoveryTraversalContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
                await Assert.That(denied?.SqlState).IsEqualTo("55000");
            }
            finally
            {
                await migration.Database.ExecuteSqlRawAsync(
                    $"ALTER FUNCTION {sql.Name("discovery_traversal_revision_read")}(uuid) VOLATILE");
            }
        }
    }

    [Test]
    public async Task TenantKeyColumnGrantDoesNotLeakOtherUserColumns()
    {
        await InstallTenantAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        var relation = sql.Table(typeof(Tenant));
        var name = sql.Column(typeof(Tenant), nameof(Tenant.FullName));
        await PostgresDiscoveryTraversalContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        var privateVisible = await migration.Database.SqlQueryRaw<bool>(
            $"SELECT has_column_privilege('{PostgresDiscoveryTraversalContract.OwnerRole}', '{relation}', '{name.Trim('"')}', 'SELECT') AS \"Value\"")
            .SingleAsync();
        await Assert.That(privateVisible).IsFalse();
        await migration.Database.ExecuteSqlRawAsync(
            $"GRANT SELECT ({name}) ON {relation} TO {PostgresDiscoveryTraversalContract.OwnerRole}");
        try
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresDiscoveryTraversalContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(denied?.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ({name}) ON {relation} FROM {PostgresDiscoveryTraversalContract.OwnerRole}");
        }
    }

    [Test]
    public async Task NativeRevisionNoOpAloneInvalidatesAnOlderSerializableReader()
    {
        Guid tenant = await InstallTenantAsync();
        await using var first = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using var second = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using (var bootstrap = await first.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await new EventDiscoveryDisclosureRepository(first).AcquireCurrentAsync(tenant, default);
            await bootstrap.CommitAsync();
        }
        await using var stale = await first.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        _ = await new EventDiscoveryIdentityRepository(first).GetRevisionAsync(tenant, default);
        string name = new HumanPrincipalSourceSql(first).Name("discovery_traversal_revision_acquire");
        await using (var winner = await second.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await second.Database.ExecuteSqlRawAsync($"SELECT * FROM {name}({{0}})", tenant);
            await winner.CommitAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            first.Database.ExecuteSqlRawAsync($"SELECT * FROM {name}({{0}})", tenant));
        await Assert.That(denied?.SqlState).IsEqualTo("40001");
        await stale.RollbackAsync();
    }

    [Test]
    public async Task ReservationFenceRejectsSerializableSnapshotPredatingAnotherReservationWrite()
    {
        Guid tenant = await InstallTenantAsync();
        await using var stale = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using var winner = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using (var bootstrap = await winner.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await new EventDiscoverySnapshotRepository(winner).AcquireFenceAsync(tenant, default);
            await bootstrap.CommitAsync();
        }
        await using var staleTransaction = await stale.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        _ = await new EventDiscoveryIdentityRepository(stale).GetRevisionAsync(tenant, default);
        await using (var changed = await winner.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await new EventDiscoverySnapshotRepository(winner).AcquireFenceAsync(tenant, default);
            await changed.CommitAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            new EventDiscoverySnapshotRepository(stale).AcquireFenceAsync(tenant, default));
        await Assert.That(denied?.SqlState).IsEqualTo("40001");
        await staleTransaction.RollbackAsync();
    }

    [Test]
    public async Task RevisionFenceRejectsSerializableSnapshotPredatingCommittedEpochAdvance()
    {
        Guid tenant = await InstallTenantAsync();
        await using var stale = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using var writer = Open(PrimaryDatabaseRole.Runtime, tenant);
        await using (var bootstrap = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(tenant, default);
            await bootstrap.CommitAsync();
        }
        await using var staleTransaction = await stale.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        _ = await new EventDiscoveryIdentityRepository(stale).GetRevisionAsync(tenant, default);
        await using (var change = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await new EventDiscoveryDisclosureRepository(writer).AdvanceAsync([tenant], default);
            await change.CommitAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            new EventDiscoveryDisclosureRepository(stale).AcquireCurrentAsync(tenant, default));
        await Assert.That(denied?.SqlState).IsEqualTo("40001");
        await staleTransaction.RollbackAsync();
    }

    [Test]
    public async Task CaptureHonorsPhysicalBudgetAndCannotReuseFenceAfterRollback()
    {
        Guid tenant = await InstallTenantAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        var snapshots = new EventDiscoverySnapshotRepository(runtime);
        var limits = new EventDiscoveryTraversalLimits(maxPhysicalItems: 1);
        EventDiscoverySnapshot proposed;
        await using (var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await snapshots.AcquireFenceAsync(tenant, default);
            var revision = await new EventDiscoveryDisclosureRepository(runtime).AcquireCurrentAsync(tenant, default);
            var now = await NowAsync(runtime);
            proposed = Snapshot(tenant, revision, now, Guid.CreateVersion7());
            await Assert.That(await snapshots.CaptureAsync(proposed, limits, now, default)).IsNotNull();
            var next = Snapshot(tenant, revision, now, Guid.CreateVersion7());
            await Assert.That(await snapshots.CaptureAsync(next, limits, now, default)).IsNull();
            await transaction.RollbackAsync();
        }
        await using var later = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            snapshots.CaptureAsync(proposed, limits, proposed.CreatedAtUtc, default));
        await later.RollbackAsync();
        await Assert.That(await snapshots.GetAsync(tenant, proposed.Id, default)).IsNull();
    }

    [Test]
    public async Task RetentionDeletesOnlyExpiredRowsOfTheExactLeaseIncludingDeletedTenantOwners()
    {
        await InstallTenantAsync();
        Guid tenant = Guid.CreateVersion7(), foreign = Guid.CreateVersion7();
        // Reservation/header lifetime deliberately does not depend on a public Tenant row.
        await using var seed = fixture.CreateDbContext();
        DateTime now = await NowAsync(seed);
        foreach (Guid owner in new[] { tenant, foreign })
        {
            seed.Set<EventDiscoverySnapshotReservation>().Add(new() { TenantId = owner });
            seed.Set<EventDiscoverySnapshot>().Add(Snapshot(owner, new(), now.AddMinutes(-10), Guid.CreateVersion7()));
            seed.Set<EventDiscoverySnapshot>().Add(Snapshot(owner, new(), now, Guid.CreateVersion7()));
        }
        await seed.SaveChangesAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        var snapshots = new EventDiscoverySnapshotRepository(runtime);
        await using (var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await snapshots.AcquireFenceAsync(tenant, default);
            await Assert.That(await snapshots.PurgeExpiredAsync(tenant, now.AddDays(1), 100, default)).IsEqualTo(1);
            await transaction.CommitAsync();
        }
        await using var foreignRuntime = Open(PrimaryDatabaseRole.Runtime, foreign);
        await using var foreignTransaction = await foreignRuntime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var foreignSnapshots = new EventDiscoverySnapshotRepository(foreignRuntime);
        await foreignSnapshots.AcquireFenceAsync(foreign, default);
        await Assert.That(await foreignSnapshots.PurgeExpiredAsync(foreign, now, 100, default)).IsEqualTo(1);
        await foreignTransaction.RollbackAsync();
    }

    [Test]
    public async Task TraversalBindingsRetainDeletedPrimaryAndAliasChainWithoutForeignRows()
    {
        Guid tenant = await InstallTenantAsync(), foreign = await InstallTenantAsync();
        var member = EventDiscoveryIdentity.Create(tenant, EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7().ToString());
        var primary = EventDiscoveryIdentity.Create(tenant, EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7().ToString());
        var other = EventDiscoveryIdentity.Create(foreign, EventDiscoverySourceKind.LocalEvent, member.SourceKey);
        primary.IsDeleted = true;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(member, primary, other);
            seed.Add(new EventDiscoveryAlias
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                MemberIdentityId = member.Id,
                PrimaryIdentityId = primary.Id,
                RelationshipRevision = 1,
                ReviewerId = Guid.CreateVersion7(),
                ReasonCode = "same-event",
                ReviewedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenant);
        var identities = new EventDiscoveryIdentityRepository(runtime);
        var bindings = await identities.GetBindingsAsync(tenant, member.SourceKind, [member.SourceKey], default);
        await Assert.That(bindings.Single().Id).IsEqualTo(member.Id);
        await Assert.That(bindings.Single().Alias!.Primary.Id).IsEqualTo(primary.Id);
        await Assert.That(bindings.Single().Alias!.Primary.IsDeleted).IsTrue();
        await Assert.That(await identities.FindAsync(tenant, primary.SourceKind, primary.SourceKey, default)).IsNull();
        await Assert.That((await identities.FindAsync(tenant, member.SourceKind, member.SourceKey, default))!.Alias!.PrimaryIdentityId)
            .IsEqualTo(primary.Id);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            identities.GetBindingsAsync(tenant, member.SourceKind, Enumerable.Repeat(member.SourceKey, 1001).ToArray(), default));
    }

    private async Task<Guid> InstallTenantAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresDiscoveryTraversalContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        Guid id = Guid.CreateVersion7();
        migration.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Native discovery",
            Slug = $"discovery-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        await migration.SaveChangesAsync();
        return id;
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        // These tests observe the first native serialization result; replay would hide the contested attempt.
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options) { TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null };
    }

    private static EventDiscoverySnapshot Snapshot(Guid tenant, EventDiscoveryRevision revision, DateTime now, Guid source)
    {
        Guid id = Guid.CreateVersion7();
        return EventDiscoverySnapshot.Create(id, tenant, id.ToString("N") + id.ToString("N"),
            revision.IdentityEpoch, revision.DisclosureEpoch, now, now.AddMinutes(5), false, true, true,
            [EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, source,
                EventDiscoveryCanonicalKind.LocalEvent, source, null)], new());
    }

    private static async Task<DateTime> NowAsync(ExploreDbContext context)
    {
        if (context.Database.GetDbConnection().State != ConnectionState.Open) await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT statement_timestamp()";
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private static async Task NativeAsync(ExploreDbContext context, string operation, Guid tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {new HumanPrincipalSourceSql(context).Name("discovery_traversal_" + operation)}(@tenant)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant });
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
