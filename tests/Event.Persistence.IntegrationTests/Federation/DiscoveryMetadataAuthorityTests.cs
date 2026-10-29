using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

/// <summary>Exercises enrolled metadata permissions and RLS, not human action admission.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class DiscoveryMetadataAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string MetadataIsolationProof = "Discovery metadata authority tests verify PostgreSQL RLS independently of EF tenant filtering.";

    [Test]
    public async Task RuntimeInsertPolicyReadinessAcceptsTheDeclaredRuntimeIdentity()
    {
        await using var caller = CreateRuntimeReadinessProbe(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await caller.Database.OpenConnectionAsync();
        await Assert.That(caller.IsPostgresRuntimeReady).IsTrue();
        await Assert.That(await caller.Database.SqlQueryRaw<bool>(
            "SELECT session_user={0} AND current_user=session_user AS \"Value\"",
            fixture.NativeQueueEnrollment.RuntimeLogin).SingleAsync()).IsTrue();
        await Assert.That(caller.TenantFilterTenantId).IsNull();
    }

    [Test]
    public async Task PhysicalRuntimeLoginCannotReplaceTheDeclaredRuntimeIdentity()
    {
        var declared = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)
            with
        { Username = fixture.NativeQueueEnrollment.GlobalLogin };
        await using var caller = CreateRuntimeReadinessProbe(declared);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => caller.Database.OpenConnectionAsync());
        await Assert.That(denied.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
        await Assert.That(caller.IsPostgresRuntimeReady).IsFalse();
    }

    [Test]
    public async Task RuntimeInterceptorDoesNotGiveASpecializedPoolRuntimeInsertAuthority()
    {
        var declared = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)
            with
        { Role = PrimaryDatabaseRole.EmailPolicyTenantMutation };
        await using var caller = CreateRuntimeReadinessProbe(declared);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => caller.Database.OpenConnectionAsync());
        await Assert.That(denied.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
        await Assert.That(caller.IsPostgresRuntimeReady).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task MetadataAdmissionRequiresTheOpenOriginalOwnerWithoutAcquiringATransaction(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await OpenGlobalAsync() : OpenRuntime(seeded.ActiveTenant);
        bool globalOnly = role == PrimaryDatabaseRole.Global;
        var connection = caller.Database.GetDbConnection();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment, globalOnly, default));
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
        await caller.Database.OpenConnectionAsync();
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment, globalOnly, default);
        await Assert.That(caller.Database.CurrentTransaction).IsNull();
        await Assert.That((await caller.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
            .AsNoTracking().SingleAsync(row => row.Id == seeded.ActiveRevision)).DisclosureEpoch).IsEqualTo(11L);
        await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment, globalOnly, default);
            await Assert.That(caller.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await Assert.That(caller.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await owner.RollbackAsync();
        });
    }

    [Test]
    public async Task MetadataAdmissionObservesCommittedPrivilegeDriftOnTheSameOpenGlobalConnector()
    {
        await using var global = await OpenGlobalAsync();
        await global.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connection.ProcessID;
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, true, default);
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        string source = $"({names.Column(typeof(Tenant), nameof(Tenant.Id))}) ON {names.Table(typeof(Tenant))}";
        string login = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        await migration.Database.ExecuteSqlRawAsync($"GRANT UPDATE {source} TO {login}");
        try
        {
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, true, default));
            await Assert.That(denied!.Message).IsEqualTo("discovery_metadata_current_owner_not_ready");
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(global.Database.CurrentTransaction).IsNull();
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"REVOKE UPDATE {source} FROM {login}");
        }
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, true, default);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
    }

    [Test]
    public async Task MetadataGrantsHaveTheExactDeclaredColumnInventory()
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        string[] expected = ["global:tenant:Id:SELECT", .. new[] { "runtime", "global" }.SelectMany(login =>
            new[] { "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:SELECT")
                .Concat(new[] { "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:INSERT"))
                .Concat(new[] { "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:UPDATE")))];
        await Assert.That(await MetadataGrantInventoryAsync(migration))
            .IsEqualTo(string.Join("|", expected.Order(StringComparer.Ordinal)));
    }

    [Test]
    public async Task MetadataPermissionsReapplyWithoutReplacingTheTransactionOrBreakingNativeOwners()
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var connection = migration.Database.GetDbConnection();
        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresDiscoveryTraversalContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresManagedProviderProvisioningContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await Assert.That(migration.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(migration.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task MetadataInstallerRejectsTenantRlsThatWouldHideTheCompleteGlobalIdAudience()
    {
        var seeded = await SeedAsync();
        await using (var global = await OpenGlobalAsync())
        {
            var baseline = await global.Tenants.AsNoTracking()
                .Where(row => row.Id == seeded.ActiveTenant || row.Id == seeded.InactiveTenant)
                .Select(row => row.Id).ToArrayAsync();
            await Assert.That(baseline.Length).IsEqualTo(2);
        }
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {names.Table(typeof(Tenant))} ENABLE ROW LEVEL SECURITY");
        // ALTER holds an exclusive relation lock. Inspect the actual policy audience in this
        // transaction instead of blocking a second connection behind our own uncommitted DDL.
        await using var proof = migration.Database.GetDbConnection().CreateCommand();
        proof.Transaction = transaction.GetDbTransaction();
        proof.CommandText = """
            SELECT c.relrowsecurity AND NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid AND p.polpermissive
                    AND p.polcmd IN ('r','*') AND (0::oid=ANY(p.polroles) OR EXISTS (
                        SELECT 1 FROM unnest(p.polroles) role_id WHERE pg_catalog.pg_has_role(@global,role_id,'USAGE'))))
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)
            """;
        proof.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(Tenant))));
        proof.Parameters.Add(new NpgsqlParameter("global", fixture.NativeQueueEnrollment.GlobalLogin));
        await Assert.That(await proof.ExecuteScalarAsync() is true).IsTrue();
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresDiscoveryMetadataPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.Message).IsEqualTo("discovery_metadata_permissions_drift");
        await Assert.That(await proof.ExecuteScalarAsync() is true).IsTrue();
        await Assert.That(migration.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SelectedGlobalLeaseUsesTenantEqualityWhileRootMetadataPoliciesRemainInstalled()
    {
        var seeded = await SeedAsync();
        await using (var root = await OpenGlobalAsync())
        {
            var baseline = await root.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                .Where(row => row.Id == seeded.ActiveRevision || row.Id == seeded.InactiveRevision)
                .Select(row => row.Id).ToArrayAsync();
            await Assert.That(baseline.Length).IsEqualTo(2);
        }
        IMixedSettingsDbContextFactory factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var selected = await factory.CreateMixedSettingsAsync(seeded.ActiveTenant, default);
        await selected.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await selected.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = selected.Database.GetDbConnection();
            string[] keys = [GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
                GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode];
            var settings = await new AtprotoFederationSettingRepository(selected, fixture.NativeQueueEnrollment)
                .GetInstanceAsync(keys);
            await Assert.That(settings.Count).IsEqualTo(3);
            await Assert.That(string.Join("|", settings.Select(row => row.SettingKey).Order(StringComparer.Ordinal)))
                .IsEqualTo(string.Join("|", keys.Order(StringComparer.Ordinal)));
            var visible = await selected.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                .Where(row => row.Id == seeded.ActiveRevision || row.Id == seeded.InactiveRevision)
                .Select(row => row.Id).ToArrayAsync();
            await Assert.That(visible.Length).IsEqualTo(1);
            await Assert.That(visible.Single()).IsEqualTo(seeded.ActiveRevision);
            int foreign = await selected.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                .Where(row => row.Id == seeded.InactiveRevision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 18L));
            await Assert.That(foreign).IsEqualTo(0);
            int own = await selected.Set<EventDiscoveryRevision>().Where(row => row.Id == seeded.ActiveRevision && row.DisclosureEpoch == 11)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 12L));
            await Assert.That(own).IsEqualTo(1);
            await Assert.That(selected.TenantFilterTenantId).IsEqualTo(seeded.ActiveTenant);
            await Assert.That(selected.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(selected.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments("table-select")]
    [Arguments("tenant-write")]
    [Arguments("tenant-read-grant-option")]
    [Arguments("tenant-public-read")]
    [Arguments("revision-key")]
    [Arguments("grant-option")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin")]
    [Arguments("migrator-admin")]
    [Arguments("read-policy")]
    [Arguments("update-policy")]
    [Arguments("missing-grant")]
    [Arguments("table-insert")]
    [Arguments("insert-grant-option")]
    [Arguments("insert-public")]
    [Arguments("insert-inherited")]
    [Arguments("missing-insert-grant")]
    [Arguments("insert-policy")]
    [Arguments("missing-insert-policy")]
    [Arguments("insert-policy-public")]
    [Arguments("runtime-insert-policy")]
    [Arguments("runtime-insert-policy-public")]
    [Arguments("missing-runtime-insert-policy")]
    public async Task MetadataPermissionDriftIsRejectedWithoutRepair(string change)
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string revisions = names.Table(typeof(EventDiscoveryRevision));
        string epoch = names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.DisclosureEpoch));
        string revisionId = names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.Id));
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string insertPolicy = names.Quote("discovery_metadata_global_insert");
        string runtimeInsertPolicy = names.Quote("discovery_metadata_runtime_insert");
        string other = names.Quote("metadata_drift_" + Guid.CreateVersion7().ToString("N"));
        await migration.Database.ExecuteSqlRawAsync(change switch
        {
            "table-select" => $"GRANT SELECT ON {revisions} TO {global}",
            "tenant-write" => $"GRANT UPDATE ({names.Column(typeof(Tenant), nameof(Tenant.Id))}) ON {names.Table(typeof(Tenant))} TO {global}",
            "tenant-read-grant-option" => $"GRANT SELECT ({names.Column(typeof(Tenant), nameof(Tenant.Id))}) ON {names.Table(typeof(Tenant))} TO {global} WITH GRANT OPTION",
            "tenant-public-read" => $"GRANT SELECT ({names.Column(typeof(Tenant), nameof(Tenant.Id))}) ON {names.Table(typeof(Tenant))} TO PUBLIC",
            "revision-key" => $"GRANT UPDATE ({names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.TenantId))}) ON {revisions} TO {global}",
            "grant-option" => $"GRANT UPDATE ({epoch}) ON {revisions} TO {global} WITH GRANT OPTION",
            "public" => $"GRANT SELECT ({epoch}) ON {revisions} TO PUBLIC",
            "inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT UPDATE ({epoch}) ON {revisions} TO {other}; GRANT {other} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "set-only" => $"CREATE ROLE {other} NOLOGIN; GRANT UPDATE ({epoch}) ON {revisions} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "admin" => $"CREATE ROLE {other} NOLOGIN; GRANT UPDATE ({epoch}) ON {revisions} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "migrator-admin" => $"GRANT {names.Quote(fixture.NativeQueueEnrollment.MigratorLogin)} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "read-policy" => $"ALTER POLICY discovery_metadata_global_read ON {revisions} USING (true)",
            "update-policy" => $"ALTER POLICY discovery_metadata_global_update ON {revisions} WITH CHECK (true)",
            "missing-grant" => $"REVOKE UPDATE ({epoch}) ON {revisions} FROM {global}",
            "table-insert" => $"GRANT INSERT ON {revisions} TO {global}",
            "insert-grant-option" => $"GRANT INSERT ({revisionId}) ON {revisions} TO {global} WITH GRANT OPTION",
            "insert-public" => $"GRANT INSERT ({revisionId}) ON {revisions} TO PUBLIC",
            "insert-inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT INSERT ({revisionId}) ON {revisions} TO {other}; GRANT {other} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "missing-insert-grant" => $"REVOKE INSERT ({revisionId}) ON {revisions} FROM {runtime}",
            "insert-policy" => $"ALTER POLICY {insertPolicy} ON {revisions} WITH CHECK (true)",
            "missing-insert-policy" => $"DROP POLICY {insertPolicy} ON {revisions}",
            "insert-policy-public" => $"ALTER POLICY {insertPolicy} ON {revisions} TO PUBLIC",
            "runtime-insert-policy" => $"ALTER POLICY {runtimeInsertPolicy} ON {revisions} WITH CHECK (true)",
            "runtime-insert-policy-public" => $"ALTER POLICY {runtimeInsertPolicy} ON {revisions} TO PUBLIC",
            "missing-runtime-insert-policy" => $"DROP POLICY {runtimeInsertPolicy} ON {revisions}",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        string before = await MetadataGrantInventoryAsync(migration);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresDiscoveryMetadataPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.Message).IsEqualTo("discovery_metadata_permissions_drift");
        await Assert.That(await MetadataGrantInventoryAsync(migration)).IsEqualTo(before);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, true)]
    [Arguments(PrimaryDatabaseRole.Runtime, false)]
    [Arguments(PrimaryDatabaseRole.Global, false)]
    public async Task MetadataAuthorityDoesNotGrantRevisionDeletion(PrimaryDatabaseRole role, bool selectedTenant)
    {
        var seeded = await SeedAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await OpenGlobalAsync() : selectedTenant ? OpenRuntime(seeded.ActiveTenant) : OpenTenantlessRuntime();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => caller.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof).Where(row => row.Id == seeded.ActiveRevision).ExecuteDeleteAsync());
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, true)]
    [Arguments(PrimaryDatabaseRole.Runtime, false)]
    [Arguments(PrimaryDatabaseRole.Global, false)]
    public async Task EnrolledCreatorInsertsCanonicalRevisionOnItsOriginalTransaction(PrimaryDatabaseRole role, bool selectedTenant)
    {
        var seeded = await SeedUninitializedMetadataAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await OpenGlobalAsync() : selectedTenant ? OpenRuntime(seeded.ActiveTenant) : OpenTenantlessRuntime();
        bool insertOnly = role == PrimaryDatabaseRole.Runtime && !selectedTenant;
        await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = (NpgsqlConnection)caller.Database.GetDbConnection();
            int backend = connection.ProcessID;
            Guid? originalTenant = caller.TenantFilterTenantId;
            if (!insertOnly)
                await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(
                    caller, fixture.NativeQueueEnrollment, role == PrimaryDatabaseRole.Global, default);
            // Exercise database authority only; the real creator's tenant receipt is covered by its own suite.
            var revision = new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(),
                TenantId = seeded.ActiveTenant,
                IdentityEpoch = 0,
                DisclosureEpoch = 0
            };
            await Assert.That(await InsertCanonicalRevisionAsync(caller, revision)).IsEqualTo(1);
            if (insertOnly)
            {
                await Assert.That(await caller.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                    .AsNoTracking().AnyAsync(row => row.TenantId == seeded.ActiveTenant)).IsFalse();
                int updated = await caller.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                    .Where(row => row.TenantId == seeded.ActiveTenant)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 1L));
                await Assert.That(updated).IsEqualTo(0);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment, false, default));
            }
            else
            {
                var stored = await caller.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
                    .AsNoTracking().SingleAsync(row => row.TenantId == seeded.ActiveTenant);
                await Assert.That(stored.Id).IsEqualTo(revision.Id);
                await Assert.That(stored.IdentityEpoch).IsEqualTo(0L);
                await Assert.That(stored.DisclosureEpoch).IsEqualTo(0L);
            }
            await Assert.That(caller.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(caller.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(transaction.GetDbTransaction().Connection).IsSameReferenceAs(connection);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(caller.TenantFilterTenantId).IsEqualTo(originalTenant);
            if (!insertOnly)
                await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(
                    caller, fixture.NativeQueueEnrollment, role == PrimaryDatabaseRole.Global, default);
            await transaction.RollbackAsync();
        });
        await using var observer = OpenMigrator();
        await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
            .AnyAsync(row => row.TenantId == seeded.ActiveTenant)).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task SelectedCreatorCannotInsertRevisionForAnotherTenant(PrimaryDatabaseRole role)
    {
        var seeded = await SeedUninitializedMetadataAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await ((IMixedSettingsDbContextFactory)new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)))
                .CreateMixedSettingsAsync(seeded.ActiveTenant, default)
            : OpenRuntime(seeded.ActiveTenant);
        await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment, false, default);
            var own = new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(),
                TenantId = seeded.ActiveTenant,
                IdentityEpoch = 0,
                DisclosureEpoch = 0
            };
            await Assert.That(await InsertCanonicalRevisionAsync(caller, own)).IsEqualTo(1);
            await Assert.That(await caller.Set<EventDiscoveryRevision>().AsNoTracking()
                .CountAsync(row => row.TenantId == seeded.ActiveTenant)).IsEqualTo(1);
            var foreign = new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(),
                TenantId = seeded.InactiveTenant,
                IdentityEpoch = 0,
                DisclosureEpoch = 0
            };
            var denied = await Assert.ThrowsAsync<PostgresException>(() => InsertCanonicalRevisionAsync(caller, foreign));
            await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await Assert.That(caller.TenantFilterTenantId).IsEqualTo(seeded.ActiveTenant);
            await Assert.That(caller.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        });
        await using var observer = OpenMigrator();
        await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
            .AnyAsync(row => row.TenantId == seeded.ActiveTenant || row.TenantId == seeded.InactiveTenant)).IsFalse();
    }

    [Test]
    public async Task RuntimeMetadataInsertionAuthorityDoesNotGrantTenantParentUpdate()
    {
        var seeded = await SeedAsync();
        await using var runtime = OpenRuntime(seeded.ActiveTenant);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Tenants
            .Where(row => row.Id == seeded.ActiveTenant)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Id, seeded.ActiveTenant)));
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<string> MetadataGrantInventoryAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        string Columns<T>(string label, params string[] properties) => string.Join(",", properties.Select(property =>
            $"('{label}','{property}','{names.ColumnName(typeof(T), property)}')"));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            WITH relations(label,relation) AS (VALUES ('tenant',@tenant::regclass),('revision',@revision::regclass)),
            fields(label,property,column_name) AS (VALUES {Columns<Tenant>("tenant", "Id")},
                {Columns<EventDiscoveryRevision>("revision", "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch")}),
            logins(label,role_id) AS (VALUES ('runtime',pg_catalog.to_regrole(@runtime)::oid),('global',pg_catalog.to_regrole(@global)::oid)),
            grants AS (
                SELECT l.label || ':' || r.label || ':' || COALESCE(f.property,a.attname) || ':' || acl.privilege_type
                    || CASE WHEN acl.is_grantable THEN ':grantable' ELSE '' END AS value
                FROM relations r JOIN pg_catalog.pg_attribute a ON a.attrelid=r.relation
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl JOIN logins l ON l.role_id=acl.grantee
                LEFT JOIN fields f ON f.label=r.label AND f.column_name=a.attname
                UNION ALL
                SELECT l.label || ':' || r.label || ':TABLE:' || acl.privilege_type
                FROM relations r JOIN pg_catalog.pg_class c ON c.oid=r.relation
                CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl JOIN logins l ON l.role_id=acl.grantee)
            SELECT value FROM grants
            """;
        command.Parameters.Add(new NpgsqlParameter("tenant", names.Table(typeof(Tenant))));
        command.Parameters.Add(new NpgsqlParameter("revision", names.Table(typeof(EventDiscoveryRevision))));
        command.Parameters.Add(new NpgsqlParameter("runtime", fixture.NativeQueueEnrollment.RuntimeLogin));
        command.Parameters.Add(new NpgsqlParameter("global", fixture.NativeQueueEnrollment.GlobalLogin));
        var actual = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
        return string.Join("|", actual.Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task RootGlobalReadsCompleteTenantIdsIncludingInactiveTenants()
    {
        var seeded = await SeedAsync();
        await using var global = await OpenGlobalAsync();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        var selected = await global.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == seeded.ActiveTenant || tenant.Id == seeded.InactiveTenant)
            .Select(tenant => tenant.Id).ToArrayAsync();

        await Assert.That(selected.Length).IsEqualTo(2);
        await Assert.That(selected.Contains(seeded.ActiveTenant)).IsTrue();
        await Assert.That(selected.Contains(seeded.InactiveTenant)).IsTrue();
        await Assert.That(global.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeAdvancesOnlyItsExpectedDisclosureEpochOnTheOriginalOwner()
    {
        var seeded = await SeedAsync();
        await using var runtime = OpenRuntime(seeded.ActiveTenant);
        await runtime.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = runtime.Database.GetDbConnection();
            var before = await runtime.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
            await Assert.That(before.Id).IsEqualTo(seeded.ActiveRevision);
            await Assert.That(before.IdentityEpoch).IsEqualTo(7L);
            await Assert.That(before.DisclosureEpoch).IsEqualTo(11L);

            int changed = await runtime.Set<EventDiscoveryRevision>()
                .Where(row => row.Id == seeded.ActiveRevision && row.TenantId == seeded.ActiveTenant
                    && row.IdentityEpoch == 7 && row.DisclosureEpoch == 11)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 12L));
            await Assert.That(changed).IsEqualTo(1);
            await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(connection);
            var after = await runtime.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
            await Assert.That(after.IdentityEpoch).IsEqualTo(7L);
            await Assert.That(after.DisclosureEpoch).IsEqualTo(12L);
            await owner.CommitAsync();
        });

        await using var observer = OpenMigrator();
        var persisted = await observer.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof).AsNoTracking()
            .SingleAsync(row => row.Id == seeded.ActiveRevision);
        await Assert.That(persisted.IdentityEpoch).IsEqualTo(7L);
        await Assert.That(persisted.DisclosureEpoch).IsEqualTo(12L);
        var foreign = await observer.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof).AsNoTracking()
            .SingleAsync(row => row.Id == seeded.InactiveRevision);
        await Assert.That(foreign.DisclosureEpoch).IsEqualTo(17L);
    }

    [Test]
    public async Task RootGlobalAdvancesInactiveTenantMetadataWithoutRetargetingItsLease()
    {
        var seeded = await SeedAsync();
        await using var global = await OpenGlobalAsync();
        await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owner = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            int changed = await global.Set<EventDiscoveryRevision>()
                .IgnoreTenantFilter(MetadataIsolationProof)
                .Where(row => row.Id == seeded.InactiveRevision && row.TenantId == seeded.InactiveTenant
                    && row.IdentityEpoch == 13 && row.DisclosureEpoch == 17)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 18L));
            await Assert.That(changed).IsEqualTo(1);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(owner);
            var actual = await global.Set<EventDiscoveryRevision>()
                .IgnoreTenantFilter(MetadataIsolationProof).AsNoTracking()
                .SingleAsync(row => row.Id == seeded.InactiveRevision);
            await Assert.That(actual.IdentityEpoch).IsEqualTo(13L);
            await Assert.That(actual.DisclosureEpoch).IsEqualTo(18L);
            await owner.RollbackAsync();
        });
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global, "tenant-system-column")]
    [Arguments(PrimaryDatabaseRole.Runtime, "tenant-description")]
    [Arguments(PrimaryDatabaseRole.Global, "user")]
    [Arguments(PrimaryDatabaseRole.Runtime, "user")]
    [Arguments(PrimaryDatabaseRole.Runtime, "user-id")]
    [Arguments(PrimaryDatabaseRole.Global, "system-setting")]
    [Arguments(PrimaryDatabaseRole.Runtime, "system-setting")]
    [Arguments(PrimaryDatabaseRole.Global, "tenant-setting")]
    [Arguments(PrimaryDatabaseRole.Runtime, "tenant-setting")]
    public async Task MetadataAuthorityDoesNotExposePrivateSourceColumns(PrimaryDatabaseRole role, string source)
    {
        var seeded = await SeedAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await OpenGlobalAsync() : OpenRuntime(seeded.ActiveTenant);
        if (source == "tenant-system-column")
        {
            // All nine mapped Tenant scalars are approved directory reads; xmin is not.
            await Assert.That(await caller.Tenants.AsNoTracking()
                .Where(row => row.Id == seeded.ActiveTenant).Select(row => row.FullName).SingleAsync())
                .IsEqualTo("Discovery metadata fixture");
        }
        Task ReadPrivateAsync() => source switch
        {
            "tenant-system-column" => caller.Database.SqlQueryRaw<string>(
                $"SELECT xmin::text AS \"Value\" FROM {new HumanPrincipalSourceSql(caller).Table(typeof(Tenant))} LIMIT 1")
                .ToArrayAsync(),
            "tenant-description" => caller.Tenants.AsNoTracking()
                .Where(row => row.Id == seeded.ActiveTenant).Select(row => row.FullName).ToArrayAsync(),
            "user" => caller.Users.AsNoTracking().Select(row => row.EmailVerified).Take(1).ToArrayAsync(),
            "user-id" => caller.Users.AsNoTracking().Select(row => row.Id).Take(1).ToArrayAsync(),
            "system-setting" => caller.SystemSettings.AsNoTracking().Select(row => row.Value).Take(1).ToArrayAsync(),
            "tenant-setting" => caller.Set<TenantSetting>().AsNoTracking()
                .Select(row => row.Value).Take(1).ToArrayAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        var denied = await Assert.ThrowsAsync<PostgresException>(ReadPrivateAsync);
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task RuntimeCannotReadForeignMetadataEvenWithoutTheEfTenantFilter()
    {
        var seeded = await SeedAsync();
        await using var runtime = OpenRuntime(seeded.ActiveTenant);
        var rows = await runtime.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof).AsNoTracking()
            .Where(row => row.Id == seeded.ActiveRevision || row.Id == seeded.InactiveRevision)
            .ToArrayAsync();
        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows.Single().TenantId).IsEqualTo(seeded.ActiveTenant);
    }

    [Test]
    public async Task RuntimeCannotAdvanceForeignMetadataEvenWithoutTheEfTenantFilter()
    {
        var seeded = await SeedAsync();
        await using var runtime = OpenRuntime(seeded.ActiveTenant);
        int changed = await runtime.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof)
            .Where(row => row.Id == seeded.InactiveRevision && row.TenantId == seeded.InactiveTenant)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, 18L));
        await Assert.That(changed).IsEqualTo(0);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, "Id")]
    [Arguments(PrimaryDatabaseRole.Runtime, "TenantId")]
    [Arguments(PrimaryDatabaseRole.Global, "Id")]
    [Arguments(PrimaryDatabaseRole.Global, "TenantId")]
    public async Task MetadataInsertionDoesNotGrantRevisionKeyUpdates(PrimaryDatabaseRole role, string property)
    {
        var seeded = await SeedAsync();
        await using var caller = role == PrimaryDatabaseRole.Global
            ? await OpenGlobalAsync() : OpenRuntime(seeded.ActiveTenant);
        var target = caller.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
            .Where(row => row.Id == seeded.ActiveRevision);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => property == "Id"
            ? target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Id, Guid.CreateVersion7()))
            : target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, seeded.InactiveTenant)));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task MetadataWriteAuthorityStillRejectsNegativeEpochs()
    {
        var seeded = await SeedAsync();
        await using var runtime = OpenRuntime(seeded.ActiveTenant);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Set<EventDiscoveryRevision>()
            .Where(row => row.Id == seeded.ActiveRevision)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, -1L)));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
        await Assert.That(denied.ConstraintName).IsEqualTo("ck_discovery_revision_epochs");
    }

    private Task<ExploreDbContext> OpenGlobalAsync() =>
        new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global))
            .CreateDbContextAsync();

    private ExploreDbContext OpenRuntime(Guid tenantId)
    {
        var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        context.BindTenantLease();
        return context;
    }

    private ExploreDbContext OpenTenantlessRuntime()
    {
        var context = fixture.CreateEnrolledTenantlessDbContext();
        context.BindTenantLease();
        return context;
    }

    private ExploreDbContext CreateRuntimeReadinessProbe(PrimaryDatabaseConnectionOptions declared)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, declared);
        // Preserve the composition snapshot but connect with the same approved physical Runtime credentials.
        // Neither the connection string nor RuntimeInstance alone is an authorization declaration.
        builder.UseNpgsql(PrimaryDatabaseConfiguration.BuildConnectionString(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).ConnectionString);
        builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private ExploreDbContext OpenMigrator()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<Seeded> SeedAsync()
    {
        Guid active = Guid.CreateVersion7(), inactive = Guid.CreateVersion7();
        await using var migration = OpenMigrator();
        migration.Tenants.AddRange(CreateTenant(active, TenantStatusEnum.Active),
            CreateTenant(inactive, TenantStatusEnum.Suspended));
        await migration.SaveChangesAsync();
        var revisions = await migration.Set<EventDiscoveryRevision>()
            .IgnoreTenantFilter(MetadataIsolationProof)
            .Where(row => row.TenantId == active || row.TenantId == inactive)
            .ToListAsync();
        var activeRevision = revisions.SingleOrDefault(row => row.TenantId == active);
        if (activeRevision is null)
        {
            activeRevision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = active };
            migration.Add(activeRevision);
        }
        var inactiveRevision = revisions.SingleOrDefault(row => row.TenantId == inactive);
        if (inactiveRevision is null)
        {
            inactiveRevision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = inactive };
            migration.Add(inactiveRevision);
        }
        activeRevision.IdentityEpoch = 7;
        activeRevision.DisclosureEpoch = 11;
        inactiveRevision.IdentityEpoch = 13;
        inactiveRevision.DisclosureEpoch = 17;
        await migration.SaveChangesAsync();
        return new(active, inactive, activeRevision.Id, inactiveRevision.Id);
    }

    private async Task<Seeded> SeedUninitializedMetadataAsync()
    {
        var seeded = await SeedAsync();
        await using var migration = OpenMigrator();
        int removed = await migration.Set<EventDiscoveryRevision>().IgnoreTenantFilter(MetadataIsolationProof)
            .Where(row => row.Id == seeded.ActiveRevision || row.Id == seeded.InactiveRevision).ExecuteDeleteAsync();
        await Assert.That(removed).IsEqualTo(2);
        return seeded;
    }

    private static Task<int> InsertCanonicalRevisionAsync(ExploreDbContext context, EventDiscoveryRevision revision)
    {
        var names = new HumanPrincipalSourceSql(context);
        string[] properties = [nameof(EventDiscoveryRevision.Id), nameof(EventDiscoveryRevision.TenantId),
            nameof(EventDiscoveryRevision.IdentityEpoch), nameof(EventDiscoveryRevision.DisclosureEpoch)];
        string columns = string.Join(',', properties.Select(property => names.Column(typeof(EventDiscoveryRevision), property)));
        // Raw enrolled credentials can initialize a missing row under these approved grants; API receipt custody
        // restricts the real creator. Parameters carry C# initialization values, never database business expressions.
        string insert = $"INSERT INTO {names.Table(typeof(EventDiscoveryRevision))} ({columns}) VALUES (@id,@tenant,@identity,@disclosure)";
        return context.Database.ExecuteSqlRawAsync(insert,
            new NpgsqlParameter("id", revision.Id), new NpgsqlParameter("tenant", revision.TenantId),
            new NpgsqlParameter("identity", revision.IdentityEpoch), new NpgsqlParameter("disclosure", revision.DisclosureEpoch));
    }

    private static Tenant CreateTenant(Guid id, TenantStatusEnum status) => new()
    {
        Id = id,
        FullName = "Discovery metadata fixture",
        Slug = $"discovery-metadata-{id:N}",
        TenantStatusId = (int)status,
        TenantStatus = null!
    };

    private sealed record Seeded(Guid ActiveTenant, Guid InactiveTenant, Guid ActiveRevision, Guid InactiveRevision);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
