using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Store = Event.Persistence.IntegrationTests.Repositories.EventDiscoverySnapshotPersistenceTests.Store;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoverySnapshotMaintenanceTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Purge_removes_members_before_headers_without_relying_on_database_cascade(bool maintenance)
    {
        await using var store = await Store.CreateAsync();
        var expired = await SeedSnapshotAsync(store, store.TenantId, Now, members: 2);
        var live = await SeedSnapshotAsync(store, store.TenantId, Now.AddMinutes(1));
        var other = await SeedSnapshotAsync(store, store.OtherTenantId, Now);
        await using var context = Open(store);
        var sql = context.GetService<ISqlGenerationHelper>();
        var header = context.Model.FindEntityType(typeof(EventDiscoverySnapshot))!;
        var member = context.Model.FindEntityType(typeof(EventDiscoverySnapshotItem))!;
        var headerMapping = StoreObjectIdentifier.Table(header.GetTableName()!, header.GetSchema());
        var memberMapping = StoreObjectIdentifier.Table(member.GetTableName()!, member.GetSchema());
        string headers = sql.DelimitIdentifier(headerMapping.Name);
        string members = sql.DelimitIdentifier(memberMapping.Name);
        string headerId = sql.DelimitIdentifier(header.FindProperty(nameof(EventDiscoverySnapshot.Id))!
            .GetColumnName(headerMapping)!);
        string headerTenant = sql.DelimitIdentifier(header.FindProperty(nameof(EventDiscoverySnapshot.TenantId))!
            .GetColumnName(headerMapping)!);
        string memberSnapshot = sql.DelimitIdentifier(member.FindProperty(nameof(EventDiscoverySnapshotItem.SnapshotId))!
            .GetColumnName(memberMapping)!);
        string memberTenant = sql.DelimitIdentifier(member.FindProperty(nameof(EventDiscoverySnapshotItem.TenantId))!
            .GetColumnName(memberMapping)!);
        string guard = $"CREATE TRIGGER require_member_first_delete BEFORE DELETE ON {headers} " +
            $"WHEN EXISTS (SELECT 1 FROM {members} WHERE {memberTenant} = OLD.{headerTenant} " +
            $"AND {memberSnapshot} = OLD.{headerId}) " +
            "BEGIN SELECT RAISE(ABORT, 'snapshot_members_remaining'); END;";
        await context.Database.ExecuteSqlRawAsync(guard);
        // SQLite runs this guard before any FK cascade. Prove it rejects a parent-first delete.
        var rejection = await Assert.ThrowsAsync<SqliteException>(async () =>
            await context.Set<EventDiscoverySnapshot>().Where(snapshot => snapshot.Id == expired.Id).ExecuteDeleteAsync());
        await Assert.That(rejection!.SqliteExtendedErrorCode).IsEqualTo(SQLitePCL.raw.SQLITE_CONSTRAINT_TRIGGER);

        var snapshots = new EventDiscoverySnapshotRepository(context);
        var repository = Maintenance(context);
        int deleted = await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            if (maintenance)
                return await repository.PurgeTenantBatchAsync(store.TenantId, Now, 1, token);
            await snapshots.AcquireFenceAsync(store.TenantId, token);
            await new EventDiscoveryDisclosureRepository(context).AcquireCurrentAsync(store.TenantId, token);
            return await snapshots.PurgeExpiredAsync(store.TenantId, Now, 1, token);
        });
        await Assert.That(deleted).IsEqualTo(1);
        await Assert.That(await snapshots.GetAsync(store.TenantId, expired.Id, default)).IsNull();
        await Assert.That((await snapshots.GetItemsAsync(store.TenantId, expired.Id, -1, 100, default)).Count).IsEqualTo(0);
        await Assert.That(await snapshots.GetAsync(store.TenantId, live.Id, default)).IsNotNull();
        await Assert.That((await snapshots.GetItemsAsync(store.TenantId, live.Id, -1, 100, default)).Count).IsEqualTo(1);
        await using var otherContext = Open(store, store.OtherTenantId);
        var otherSnapshots = new EventDiscoverySnapshotRepository(otherContext);
        await Assert.That(await otherSnapshots.GetAsync(store.OtherTenantId, other.Id, default)).IsNotNull();
        await Assert.That((await otherSnapshots.GetItemsAsync(store.OtherTenantId, other.Id, -1, 100, default)).Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Tenant_enumeration_is_bounded_distinct_and_seeks_across_ambient_tenant_scope()
    {
        await using var store = await Store.CreateAsync();
        Guid[] additional = await AddTenantsAsync(store, 6);
        Guid[] expiredTenants = [store.TenantId, store.OtherTenantId, .. additional.Take(4)];
        foreach (Guid tenantId in expiredTenants)
            await SeedSnapshotAsync(store, tenantId, Now);
        await SeedSnapshotAsync(store, expiredTenants[0], Now);
        await SeedSnapshotAsync(store, additional[4], Now.AddMinutes(1));
        // additional[5] has no snapshots and must not consume a maintenance tenant slot.
        Guid[] ordered = expiredTenants.Order().ToArray();
        await using var context = Open(store);
        var repository = Maintenance(context);
        var first = await repository.GetExpiredOwnersAsync(null, Now, 2, default);
        var second = await repository.GetExpiredOwnersAsync(first[^1].TenantId, Now, 2, default);
        var third = await repository.GetExpiredOwnersAsync(second[^1].TenantId, Now, 2, default);
        var end = await repository.GetExpiredOwnersAsync(third[^1].TenantId, Now, 2, default);
        await Assert.That(first.Count).IsEqualTo(2);
        await Assert.That(second.Count).IsEqualTo(2);
        await Assert.That(third.Count).IsEqualTo(2);
        await Assert.That(end.Count).IsEqualTo(0);
        await Assert.That(first.Concat(second).Concat(third).Select(owner => owner.TenantId).SequenceEqual(ordered)).IsTrue();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(store.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    [Test]
    public async Task Handler_keyset_progress_wraps_back_to_a_tenant_with_more_than_one_snapshot_batch()
    {
        await using var store = await Store.CreateAsync();
        Guid[] tenants = (await AddTenantsAsync(store, 6)).Order().ToArray();
        for (int index = 0; index < 11; index++)
            await SeedSnapshotAsync(store, tenants[0], Now);
        foreach (Guid tenantId in tenants.Skip(1))
            await SeedSnapshotAsync(store, tenantId, Now);
        EventDiscoverySnapshot live = await SeedSnapshotAsync(store, tenants[0], Now.AddMinutes(1));
        await using var context = Open(store);
        var repository = Maintenance(context);
        var handler = new PurgeEventDiscoverySnapshotsCommandHandler(
            repository, new EfCoreUnitOfWork(context), new FixedClock());

        var first = await handler.ExecuteAsync(new());
        await Assert.That(first.DeletedSnapshots).IsEqualTo(14);
        await Assert.That(first.NextTenantId).IsEqualTo(tenants[4]);
        var remaining = await repository.GetExpiredOwnersAsync(null, Now, 5, default);
        await Assert.That(remaining.Select(owner => owner.TenantId).SequenceEqual(new[] { tenants[0], tenants[5] })).IsTrue();

        var second = await handler.ExecuteAsync(new(first.NextTenantId));
        await Assert.That(second.DeletedSnapshots).IsEqualTo(1);
        await Assert.That(second.NextTenantId).IsNull();
        var wrapped = await handler.ExecuteAsync(new(second.NextTenantId));
        await Assert.That(wrapped.DeletedSnapshots).IsEqualTo(1);
        await Assert.That(wrapped.NextTenantId).IsNull();
        var exhausted = await handler.ExecuteAsync(new(wrapped.NextTenantId));
        await Assert.That(exhausted.DeletedSnapshots).IsEqualTo(0);
        await Assert.That(exhausted.NextTenantId).IsNull();
        await using var tenantReader = Open(store, tenants[0]);
        await Assert.That(await new EventDiscoverySnapshotRepository(tenantReader)
            .GetAsync(tenants[0], live.Id, default)).IsNotNull();
    }

    [Test]
    public async Task Purge_deletes_only_the_exact_tenants_expired_batch_and_cascades_its_members()
    {
        await using var store = await Store.CreateAsync();
        var older = await SeedSnapshotAsync(store, store.TenantId, Now.AddTicks(-1), members: 2);
        var boundary = await SeedSnapshotAsync(store, store.TenantId, Now);
        var live = await SeedSnapshotAsync(store, store.TenantId, Now.AddTicks(10));
        var other = await SeedSnapshotAsync(store, store.OtherTenantId, Now);
        await using var context = Open(store, store.OtherTenantId);
        var repository = Maintenance(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        int deleted = await unitOfWork.ExecuteSerializableAsync(
            token => repository.PurgeTenantBatchAsync(store.TenantId, Now, 1, token));
        await Assert.That(deleted).IsEqualTo(1);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(store.OtherTenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();

        await using var primaryReader = Open(store);
        var primary = new EventDiscoverySnapshotRepository(primaryReader);
        await Assert.That(await primary.GetAsync(store.TenantId, older.Id, default)).IsNull();
        await Assert.That((await primary.GetItemsAsync(store.TenantId, older.Id, -1, 100, default)).Count).IsEqualTo(0);
        await Assert.That(await primary.GetAsync(store.TenantId, boundary.Id, default)).IsNotNull();
        await Assert.That(await primary.GetAsync(store.TenantId, live.Id, default)).IsNotNull();
        var otherReader = new EventDiscoverySnapshotRepository(context);
        await Assert.That(await otherReader.GetAsync(store.OtherTenantId, other.Id, default)).IsNotNull();
        await Assert.That((await otherReader.GetItemsAsync(store.OtherTenantId, other.Id, -1, 100, default)).Count)
            .IsEqualTo(1);

        deleted = await unitOfWork.ExecuteSerializableAsync(
            token => repository.PurgeTenantBatchAsync(store.TenantId, Now, 10, token));
        await Assert.That(deleted).IsEqualTo(1);
        await Assert.That(await primary.GetAsync(store.TenantId, boundary.Id, default)).IsNull();
        await Assert.That(await primary.GetAsync(store.TenantId, live.Id, default)).IsNotNull();
        await Assert.That(await otherReader.GetAsync(store.OtherTenantId, other.Id, default)).IsNotNull();
    }

    [Test]
    public async Task Inactive_and_purged_tenants_remain_eligible_for_expired_retention_cleanup()
    {
        await using var store = await Store.CreateAsync();
        TenantStatusEnum[] inactiveStates =
            [TenantStatusEnum.Provisioning, TenantStatusEnum.Suspended, TenantStatusEnum.Archived, TenantStatusEnum.Purged];
        Guid[] inactiveTenants = await AddTenantsAsync(store, inactiveStates.Length, inactiveStates);
        Guid[] tenants = [store.TenantId, .. inactiveTenants];
        var expired = new List<EventDiscoverySnapshot>();
        var live = new List<EventDiscoverySnapshot>();
        foreach (Guid tenantId in tenants)
        {
            expired.Add(await SeedSnapshotAsync(store, tenantId, Now));
            live.Add(await SeedSnapshotAsync(store, tenantId, Now.AddMinutes(1)));
        }
        await using var context = Open(store, store.OtherTenantId);
        var repository = Maintenance(context);
        var candidates = await repository.GetExpiredOwnersAsync(null, Now, 5, default);
        await Assert.That(candidates.Count).IsEqualTo(5);
        var sourceTenants = await context.Tenants.AsNoTracking()
            .Where(tenant => tenants.Contains(tenant.Id)).ToArrayAsync();
        await Assert.That(sourceTenants.Count(tenant => !tenant.IsActive)).IsEqualTo(4);
        await Assert.That(candidates.Select(owner => owner.TenantId).ToHashSet().SetEquals(tenants)).IsTrue();
        var handler = new PurgeEventDiscoverySnapshotsCommandHandler(
            repository, new EfCoreUnitOfWork(context), new FixedClock());
        var result = await handler.ExecuteAsync(new());
        await Assert.That(result.DeletedSnapshots).IsEqualTo(5);
        await Assert.That((await repository.GetExpiredOwnersAsync(null, Now, 5, default)).Count).IsEqualTo(0);
        foreach (var snapshot in expired)
        {
            await using var reader = Open(store, snapshot.TenantId);
            var snapshots = new EventDiscoverySnapshotRepository(reader);
            await Assert.That(await snapshots.GetAsync(snapshot.TenantId, snapshot.Id, default)).IsNull();
            await Assert.That((await snapshots.GetItemsAsync(snapshot.TenantId, snapshot.Id, -1, 100, default)).Count)
                .IsEqualTo(0);
        }
        foreach (var snapshot in live)
        {
            await using var reader = Open(store, snapshot.TenantId);
            await Assert.That(await new EventDiscoverySnapshotRepository(reader)
                .GetAsync(snapshot.TenantId, snapshot.Id, default)).IsNotNull();
        }
    }

    [Test]
    public async Task Enumeration_keeps_expired_membership_discoverable_after_the_tenant_source_is_deleted()
    {
        await using var store = await Store.CreateAsync();
        var snapshot = await SeedSnapshotAsync(store, store.TenantId, Now);
        await DeleteTenantSourceAsync(store, store.TenantId);
        await using var context = Open(store, store.OtherTenantId);
        var candidates = await Maintenance(context).GetExpiredOwnersAsync(null, Now, 5, default);
        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].TenantId).IsEqualTo(snapshot.TenantId);
    }

    [Test]
    public async Task Deleted_tenant_cleanup_does_not_recreate_authority_or_delete_live_membership()
    {
        await using var store = await Store.CreateAsync();
        var expired = await SeedSnapshotAsync(store, store.TenantId, Now, members: 2);
        var live = await SeedSnapshotAsync(store, store.TenantId, Now.AddMinutes(1));
        var other = await SeedSnapshotAsync(store, store.OtherTenantId, Now);
        await DeleteTenantSourceAsync(store, store.TenantId);
        await using var context = Open(store, store.OtherTenantId);
        var repository = Maintenance(context);
        int deleted = await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(
            token => repository.PurgeTenantBatchAsync(store.TenantId, Now, 10, token));
        await Assert.That(deleted).IsEqualTo(1);
        await using var reader = Open(store);
        var snapshots = new EventDiscoverySnapshotRepository(reader);
        await Assert.That(await snapshots.GetAsync(store.TenantId, expired.Id, default)).IsNull();
        await Assert.That((await snapshots.GetItemsAsync(store.TenantId, expired.Id, -1, 100, default)).Count).IsEqualTo(0);
        await Assert.That(await snapshots.GetAsync(store.TenantId, live.Id, default)).IsNotNull();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
        await Assert.That(await reader.Tenants.AnyAsync(tenant => tenant.Id == store.TenantId)).IsFalse();
        await Assert.That(await new EventDiscoverySnapshotRepository(context)
            .GetAsync(store.OtherTenantId, other.Id, default)).IsNotNull();
    }

    [Test]
    public async Task Missing_public_revision_does_not_prevent_cleanup_or_bootstrap_new_authority()
    {
        await using var store = await Store.CreateAsync();
        var expired = await SeedSnapshotAsync(store, store.TenantId, Now, members: 2);
        var live = await SeedSnapshotAsync(store, store.TenantId, Now.AddMinutes(1));
        await using var context = Open(store);
        await context.Set<EventDiscoveryRevision>().ExecuteDeleteAsync();
        var handler = new PurgeEventDiscoverySnapshotsCommandHandler(
            Maintenance(context), new EfCoreUnitOfWork(context), new FixedClock());

        var result = await handler.ExecuteAsync(new());

        await Assert.That(result.DeletedSnapshots).IsEqualTo(1);
        await Assert.That(await context.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
        await Assert.That(await context.Tenants.AnyAsync(tenant => tenant.Id == store.TenantId)).IsTrue();
        var snapshots = new EventDiscoverySnapshotRepository(context);
        await Assert.That(await snapshots.GetAsync(store.TenantId, expired.Id, default)).IsNull();
        await Assert.That((await snapshots.GetItemsAsync(store.TenantId, expired.Id, -1, 100, default)).Count)
            .IsEqualTo(0);
        await Assert.That(await snapshots.GetAsync(store.TenantId, live.Id, default)).IsNotNull();
        await Assert.That((await snapshots.GetItemsAsync(store.TenantId, live.Id, -1, 100, default)).Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Failed_transaction_restores_membership_and_leaves_other_tenant_and_scope_untouched()
    {
        await using var store = await Store.CreateAsync();
        var first = await SeedSnapshotAsync(store, store.TenantId, Now, members: 2);
        var second = await SeedSnapshotAsync(store, store.TenantId, Now);
        var other = await SeedSnapshotAsync(store, store.OtherTenantId, Now);
        await using var context = Open(store, store.OtherTenantId);
        var repository = Maintenance(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        await Assert.ThrowsAsync<RollbackProbeException>(async () =>
            await unitOfWork.ExecuteSerializableAsync<int>(async token =>
            {
                await Assert.That(await repository.PurgeTenantBatchAsync(store.TenantId, Now, 2, token))
                    .IsEqualTo(2);
                throw new RollbackProbeException();
            }));
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(store.OtherTenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await using var primaryContext = Open(store);
        var primary = new EventDiscoverySnapshotRepository(primaryContext);
        await Assert.That(await primary.GetAsync(store.TenantId, first.Id, default)).IsNotNull();
        await Assert.That(await primary.GetAsync(store.TenantId, second.Id, default)).IsNotNull();
        await Assert.That((await primary.GetItemsAsync(store.TenantId, first.Id, -1, 100, default)).Count).IsEqualTo(2);
        await Assert.That((await primary.GetItemsAsync(store.TenantId, second.Id, -1, 100, default)).Count).IsEqualTo(1);

        await Assert.That(await unitOfWork.ExecuteSerializableAsync(
            token => repository.PurgeTenantBatchAsync(store.TenantId, Now, 2, token))).IsEqualTo(2);
        await Assert.That(await primary.GetAsync(store.TenantId, first.Id, default)).IsNull();
        var otherReader = new EventDiscoverySnapshotRepository(context);
        await Assert.That(await otherReader.GetAsync(store.OtherTenantId, other.Id, default)).IsNotNull();
        await Assert.That((await otherReader.GetItemsAsync(store.OtherTenantId, other.Id, -1, 100, default)).Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Invalid_bounds_and_missing_transaction_fail_without_deleting_membership()
    {
        await using var store = await Store.CreateAsync();
        var snapshot = await SeedSnapshotAsync(store, store.TenantId, Now);
        await using var context = Open(store);
        var repository = Maintenance(context);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.GetExpiredOwnersAsync(null, Now, 6, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.GetExpiredOwnersAsync(null, Now, 0, default));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.GetExpiredOwnersAsync(null, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), 1, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.PurgeTenantBatchAsync(store.TenantId, Now, 11, default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.PurgeTenantBatchAsync(store.TenantId, Now, 1, default));
        await Assert.That(await new EventDiscoverySnapshotRepository(context)
            .GetAsync(store.TenantId, snapshot.Id, default)).IsNotNull();
    }

    [Test]
    [RequiresSnapshotMaintenancePostgreSql]
    [NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
    public async Task Restricted_PostgreSql_runtime_enumerates_only_bounded_ownership_and_purges_orphans_under_forced_rls()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var runtimeOptions = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        var migrationOptions = PrimaryDatabaseConfiguration.BindMigrator(configuration);
        await Assert.That(migrationOptions.Provider).IsEqualTo(PrimaryDatabaseProvider.PostgreSql);
        await Assert.That((migrationOptions.Host, migrationOptions.Port, migrationOptions.Database, migrationOptions.Schema))
            .IsEqualTo((runtimeOptions.Host, runtimeOptions.Port, runtimeOptions.Database, runtimeOptions.Schema));
        var runtime = PrimaryDatabaseProviderBehaviorFixture.Create(runtimeOptions);
        var migration = PrimaryDatabaseProviderBehaviorFixture.Create(migrationOptions);
        // Data queries need the configured runtime namespace rather than the
        // canonical design-time migration namespace; credentials remain migrator-owned.
        var seedAuthority = PrimaryDatabaseProviderBehaviorFixture.Create(
            migrationOptions with { Role = PrimaryDatabaseRole.Runtime });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        await using var installer = migration.CreateSystemContext();
        // Replay the native bootstrap, using only the configured migration authority.
        await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(installer, token);
        await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(installer, token);
        var sql = installer.GetService<ISqlGenerationHelper>();
        var header = installer.Model.FindEntityType(typeof(EventDiscoverySnapshot))!;
        string headers = sql.DelimitIdentifier(header.GetTableName()!, runtimeOptions.Schema);
        var member = installer.Model.FindEntityType(typeof(EventDiscoverySnapshotItem))!;
        string members = sql.DelimitIdentifier(member.GetTableName()!, runtimeOptions.Schema);
        string[] tables = new[]
        {
            typeof(EventDiscoverySnapshot), typeof(EventDiscoverySnapshotItem), typeof(EventDiscoverySnapshotReservation)
        }.Select(type => installer.Model.FindEntityType(type)!.GetTableName()!).ToArray();
        await Assert.That(await installer.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_class AS t
            JOIN pg_catalog.pg_namespace AS n ON n.oid = t.relnamespace
            WHERE n.nspname = {0} AND t.relname = ANY({1}) AND t.relrowsecurity AND t.relforcerowsecurity
            """, runtimeOptions.Schema, tables).SingleAsync(token)).IsEqualTo(3);

        // One private UUID range makes the five-owner seek assertion independent
        // of unrelated fixtures elsewhere in the shared provider database.
        string prefix = Guid.CreateVersion7().ToString("N")[..28];
        Guid lower = Guid.ParseExact(prefix + "0000", "N");
        Guid[] owners = Enumerable.Range(1, 6)
            .Select(index => Guid.ParseExact(prefix + index.ToString("x4", System.Globalization.CultureInfo.InvariantCulture), "N")).ToArray();
        Guid liveOnlyOwner = Guid.ParseExact(prefix + "0007", "N");
        DateTime now;
        await using (var clock = seedAuthority.CreateSystemContext())
            now = await clock.Database.SqlQueryRaw<DateTime>(
                """SELECT statement_timestamp() AS "Value" """).SingleAsync(token);
        try
        {
            foreach (Guid owner in owners.Append(liveOnlyOwner))
            {
                await using var seed = seedAuthority.CreateTenantContext(owner, PostgresTenantSessionInterceptor.Instance);
                var snapshots = new EventDiscoverySnapshotRepository(seed);
                await new EfCoreUnitOfWork(seed).ExecuteSerializableAsync(async ct =>
                {
                    await snapshots.AcquireFenceAsync(owner, ct);
                    int count = owner == liveOnlyOwner ? 0 : owner == owners[0] ? 11 : 1;
                    for (int index = 0; index <= count; index++)
                    {
                        DateTime expires = index == count ? now.AddMinutes(5) : now.AddMinutes(-1);
                        Guid id = Guid.CreateVersion7();
                        var snapshot = EventDiscoverySnapshot.Create(id, owner,
                            id.ToString("N") + id.ToString("N"), 0, 0, expires.AddMinutes(-15), expires,
                            false, true, true,
                            [EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7(),
                                EventDiscoveryCanonicalKind.LocalEvent, Guid.CreateVersion7(), Guid.CreateVersion7())], new());
                        await Assert.That(await snapshots.CaptureAsync(snapshot, new(), snapshot.CreatedAtUtc, ct))
                            .IsNotNull();
                    }
                    return true;
                }, token);
            }

            await using var context = runtime.CreateTenantContext(Guid.CreateVersion7(), PostgresTenantSessionInterceptor.Instance);
            await context.Database.OpenConnectionAsync(token);
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                """
                SELECT NOT rolsuper AND NOT rolbypassrls AS "Value"
                FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER
                """).SingleAsync(token)).IsTrue();
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                """
                SELECT NOT pg_has_role(CURRENT_USER, {0}, 'MEMBER')
                    AND NOT pg_has_role(CURRENT_USER, {1}, 'MEMBER')
                    AND pg_has_role(CURRENT_USER, {2}, 'USAGE') AS "Value"
                """, PostgresDiscoverySnapshotMaintenanceContract.OwnerRole,
                PostgresDiscoverySnapshotMaintenanceContract.MigratorRole,
                PostgresDiscoverySnapshotMaintenanceContract.RuntimeRole).SingleAsync(token)).IsTrue();
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                """
                SELECT NOT has_column_privilege({0}, {1}, 'criteria_hash', 'SELECT')
                    AND NOT has_table_privilege({0}, {2}, 'SELECT') AS "Value"
                """, PostgresDiscoverySnapshotMaintenanceContract.OwnerRole, headers, members)
                .SingleAsync(token)).IsTrue();
            string signature = PostgresDiscoverySnapshotMaintenanceContract.FunctionSql(context) +
                "(uuid,timestamp with time zone,integer)";
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                """
                SELECT p.prosecdef AND p.proconfig @> ARRAY['search_path=pg_catalog']
                    AND NOT EXISTS (
                        SELECT 1 FROM aclexplode(COALESCE(p.proacl, acldefault('f', p.proowner))) AS acl
                        WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE') AS "Value"
                FROM pg_catalog.pg_proc AS p WHERE p.oid = {0}::regprocedure
                """, signature).SingleAsync(token)).IsTrue();
            await Assert.That(await context.Set<EventDiscoverySnapshot>().IgnoreQueryFilters()
                .CountAsync(row => owners.Contains(row.TenantId), token)).IsEqualTo(0);
            await Assert.That(await context.Set<EventDiscoverySnapshotItem>().IgnoreQueryFilters()
                .CountAsync(row => owners.Contains(row.TenantId), token)).IsEqualTo(0);
            await Assert.That(await context.Set<EventDiscoverySnapshotReservation>().IgnoreQueryFilters()
                .CountAsync(row => owners.Contains(row.TenantId), token)).IsEqualTo(0);

            var repository = Maintenance(context);
            var first = await repository.GetExpiredOwnersAsync(lower, now, 5, token);
            await Assert.That(first.Select(owner => owner.TenantId).SequenceEqual(owners.Take(5))).IsTrue();
            var last = await repository.GetExpiredOwnersAsync(first[^1].TenantId, now, 1, token);
            await Assert.That(last.Single().TenantId).IsEqualTo(owners[5]);
            // The database boundary, not only the C# guard, rejects oversized reads.
            string oversizedRead = $"SELECT * FROM {PostgresDiscoverySnapshotMaintenanceContract.FunctionSql(context)}" +
                "({0}::uuid, {1}, 6)";
            var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                await context.Database.ExecuteSqlRawAsync(oversizedRead, [lower, now], token));
            await Assert.That(rejected!.SqlState).IsEqualTo("22023");

            DateTime future = new(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var futureFirst = await repository.GetExpiredOwnersAsync(lower, future, 5, token);
            await Assert.That(futureFirst.Select(owner => owner.TenantId).SequenceEqual(owners.Take(5))).IsTrue();
            var futureLast = await repository.GetExpiredOwnersAsync(futureFirst[^1].TenantId, future, 5, token);
            await Assert.That(futureLast.Select(owner => owner.TenantId).SequenceEqual([owners[5]])).IsTrue();
            await Assert.That(await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(
                ct => repository.PurgeTenantBatchAsync(liveOnlyOwner, future, 10, ct), token)).IsEqualTo(0);
            var handler = new PurgeEventDiscoverySnapshotsCommandHandler(repository, new EfCoreUnitOfWork(context), new FixedClock(now));
            var purged = await handler.ExecuteAsync(new(lower), token);
            await Assert.That(purged.DeletedSnapshots).IsEqualTo(14);
            await Assert.That(purged.NextTenantId).IsEqualTo(owners[4]);
            await Assert.That(await context.Set<EventDiscoverySnapshot>().IgnoreQueryFilters()
                .CountAsync(row => owners.Contains(row.TenantId), token)).IsEqualTo(0);
            await Assert.That(await context.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.current_tenant_id') AS \"Value\"").SingleAsync(token))
                .IsEqualTo(context.TenantFilterTenantId!.Value.ToString("D"));
            await using var reader = runtime.CreateTenantContext(owners[0], PostgresTenantSessionInterceptor.Instance);
            await Assert.That(await reader.Set<EventDiscoverySnapshot>().CountAsync(token)).IsEqualTo(2);
            await Assert.That(await reader.Set<EventDiscoverySnapshotItem>().CountAsync(token)).IsEqualTo(2);
            await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync(token)).IsFalse();
            await Assert.That(await reader.Tenants.AnyAsync(row => owners.Contains(row.Id), token)).IsFalse();
            await using var untouched = runtime.CreateTenantContext(owners[5], PostgresTenantSessionInterceptor.Instance);
            await Assert.That(await untouched.Set<EventDiscoverySnapshot>().CountAsync(token)).IsEqualTo(2);
            await Assert.That(await untouched.Set<EventDiscoverySnapshotItem>().CountAsync(token)).IsEqualTo(2);
        }
        finally
        {
            foreach (Guid owner in owners.Append(liveOnlyOwner))
            {
                await using var cleanup = seedAuthority.CreateTenantContext(owner, PostgresTenantSessionInterceptor.Instance);
                await new EfCoreUnitOfWork(cleanup).ExecuteSerializableAsync(async ct =>
                {
                    await cleanup.Set<EventDiscoverySnapshotItem>().Where(row => row.TenantId == owner).ExecuteDeleteAsync(ct);
                    await cleanup.Set<EventDiscoverySnapshot>().Where(row => row.TenantId == owner).ExecuteDeleteAsync(ct);
                    await cleanup.Set<EventDiscoverySnapshotReservation>().Where(row => row.TenantId == owner).ExecuteDeleteAsync(ct);
                    return true;
                }, CancellationToken.None);
            }
        }
    }

    private static ExploreDbContext Open(Store store, Guid? tenantId = null) =>
        store.Open(tenantId, SqliteNamedLockTransactionInterceptor.Instance);

    private static EventDiscoverySnapshotMaintenanceRepository Maintenance(ExploreDbContext context) =>
        new(context);

    private static async Task DeleteTenantSourceAsync(Store store, Guid tenantId)
    {
        await using var context = Open(store, tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();
        // Membership has no public-source FK by design; remove the source authority in dependency order.
        await context.Set<EventDiscoveryRevision>().Where(revision => revision.TenantId == tenantId).ExecuteDeleteAsync();
        await context.Tenants.Where(tenant => tenant.Id == tenantId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    private static async Task<Guid[]> AddTenantsAsync(
        Store store, int count, TenantStatusEnum[]? statuses = null)
    {
        Guid[] ids = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        await using var context = Open(store);
        foreach (TenantStatusEnum status in (statuses ?? []).Distinct())
            context.Set<TenantStatus>().Add(new TenantStatus
            {
                Id = (int)status, MasterCode = status.ToString(), FullName = status.ToString(),
                IsActiveState = status == TenantStatusEnum.Active
            });
        for (int index = 0; index < ids.Length; index++)
            context.Tenants.Add(new Tenant
            {
                Id = ids[index], Slug = $"maintenance-{ids[index]:N}", FullName = "Maintenance tenant",
                TenantStatusId = (int)(statuses?[index] ?? TenantStatusEnum.Active), TenantStatus = null!
            });
        await context.SaveChangesAsync();
        return ids;
    }

    private static async Task<EventDiscoverySnapshot> SeedSnapshotAsync(
        Store store, Guid tenantId, DateTime expires, int members = 1)
    {
        Guid id = Guid.CreateVersion7();
        DateTime created = expires.AddMinutes(-15);
        string criteria = id.ToString("N") + id.ToString("N");
        var items = Enumerable.Range(0, members).Select(_ =>
            EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7(),
                EventDiscoveryCanonicalKind.LocalEvent, Guid.CreateVersion7(), Guid.CreateVersion7())).ToArray();
        await using var context = Open(store, tenantId);
        var snapshots = new EventDiscoverySnapshotRepository(context);
        var disclosure = new EventDiscoveryDisclosureRepository(context);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await snapshots.AcquireFenceAsync(tenantId, token);
            var revision = await disclosure.AcquireCurrentAsync(tenantId, token);
            var snapshot = EventDiscoverySnapshot.Create(id, tenantId, criteria,
                revision.IdentityEpoch, revision.DisclosureEpoch, created, expires,
                false, true, true, items, new());
            return await snapshots.CaptureAsync(snapshot, new(), created, token)
                ?? throw new InvalidOperationException("Fixture snapshot budget was unexpectedly exhausted.");
        });
    }

    private sealed class FixedClock(DateTime? instant = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(instant ?? Now);
    }

    private sealed class RollbackProbeException : Exception;
}

internal sealed class RequiresSnapshotMaintenancePostgreSqlAttribute()
    : SkipAttribute("Requires the explicit PostgreSQL runtime and migrator authorities, with maintenance EXECUTE granted.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext _) =>
        Task.FromResult(!string.Equals(Environment.GetEnvironmentVariable("Database__Provider"),
            nameof(PrimaryDatabaseProvider.PostgreSql), StringComparison.OrdinalIgnoreCase));
}
