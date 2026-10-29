using Explore.Persistence.Services;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresInstanceStorageAdministrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SelectedGlobalReportingReturnsExactTwoTenantAggregatesWithoutPrivateTableAccess()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();
        await AssertOrdinaryAsync(global);
        await RequireDeniedAsync(global, $"SELECT * FROM {rows.ObjectsTable}");
        await RequireDeniedAsync(global, $"UPDATE {rows.CountersTable} SET used_bytes = 0");
        var counters = await new InstanceStorageAdministration(rows.Global, new NoRequestAccountingOwners())
            .ReadUsageAsync(CancellationToken.None);
        await Assert.That(counters.Sum(row => row.UsedBytes)).IsEqualTo(35);
        await Assert.That(counters.Sum(row => row.ReservedBytes)).IsEqualTo(10);
        await Assert.That(counters.Sum(row => row.QuarantinedBytes)).IsEqualTo(16);
        await Assert.That(counters.Sum(row => row.ObjectCount)).IsEqualTo(5);
        await Assert.That(global.TenantContext).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task RecalculationUsesFreshExactTenantScopesAndPreservesPhysicalCustody()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var request = await rows.Runtime.CreateDbContextAsync(rows.A);
        await AssertOrdinaryAsync(request);
        await Assert.That(await request.StorageObjects.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
        // This fixture owns native column grants and physical accounting, not an HTTP
        // credential. Authentic original-action controls live in the API accounting fixture.
        var usage = await ExecuteNativeRepairAsync(rows.Global);

        const long chargeableBytes = 101 + 107 + 109 + 103 + 23;
        await Assert.That(usage.Sum(row => row.UsedBytes)).IsEqualTo(chargeableBytes);
        await Assert.That(usage.Sum(row => row.ReservedBytes)).IsEqualTo(62);
        await Assert.That(usage.Sum(row => row.QuarantinedBytes)).IsEqualTo(17);
        await Assert.That(usage.Sum(row => row.ObjectCount)).IsEqualTo(6);
        await Assert.That(usage.Count).IsEqualTo(2);
        var local = usage.Single(row => row.Provider == StorageProviders.Local);
        await Assert.That(local.UsedBytes).IsEqualTo(chargeableBytes);
        await Assert.That(local.ReservedBytes).IsEqualTo(31);
        await Assert.That(local.QuarantinedBytes).IsEqualTo(17);
        await Assert.That(local.ObjectCount).IsEqualTo(6);
        var s3 = usage.Single(row => row.Provider == StorageProviders.S3Compatible);
        await Assert.That(s3.UsedBytes).IsEqualTo(0);
        await Assert.That(s3.ReservedBytes).IsEqualTo(31);
        await Assert.That(s3.ObjectCount).IsEqualTo(0);
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.A);
        await Assert.That(request.ChangeTracker.Entries().Count()).IsEqualTo(0);

        await using var verify = fixture.CreateDbContext();
        var held = await verify.StorageObjects.IgnoreQueryFilters().SingleAsync(row => row.Id == rows.HeldId);
        await Assert.That(held.IsDeleted).IsTrue();
        await Assert.That(held.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(held.ObjectKey).IsEqualTo(rows.HeldKey);
        await Assert.That(held.ConcurrencyStamp).IsEqualTo(rows.HeldStamp);
        await Assert.That(await verify.StorageObjectDeletionTombstones.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.StorageUploadSessions.CountAsync()).IsEqualTo(6);
        await Assert.That(await verify.StorageObjects.IgnoreQueryFilters().CountAsync()).IsEqualTo(9);
        var quarantine = await verify.StorageObjects.SingleAsync(row => row.Size == 17);
        await Assert.That(quarantine.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(quarantine.QuarantineReason).IsEqualTo("Bounded native accounting");
        await Assert.That((await verify.StorageUploadSessions.SingleAsync(row => row.ReservedBytes == 11)).Status)
            .IsEqualTo(StorageUploadSessionStates.Uploading);
        await Assert.That((await verify.StorageUploadSessions.SingleAsync(row => row.ReservedBytes == 701)).Status)
            .IsEqualTo(StorageUploadSessionStates.Failed);
        await Assert.That((await verify.StorageUploadSessions.SingleAsync(row => row.ReservedBytes == 709)).Status)
            .IsEqualTo(StorageUploadSessionStates.Canceled);
        var reservationOnly = await verify.StorageUsageCounters.SingleAsync(
            row => row.TenantId == rows.B && row.Provider == StorageProviders.S3Compatible);
        await Assert.That(reservationOnly.ReservedBytes).IsEqualTo(31);
        await Assert.That(reservationOnly.UsedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeLeaseCannotReadOrMutateForeignAccountingOrHeldObjects()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var request = await rows.Runtime.CreateDbContextAsync(rows.B);
        await AssertOrdinaryAsync(request);
        await Assert.That(await request.StorageUsageCounters.IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == rows.A)).IsEqualTo(0);
        await Assert.That(await request.StorageObjects.IgnoreQueryFilters()
            .CountAsync(row => row.Id == rows.HeldId)).IsEqualTo(0);
        await Assert.That(await request.StorageUsageCounters.IgnoreQueryFilters()
            .Where(row => row.TenantId == rows.A)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UsedBytes, 0)))
            .IsEqualTo(0);
        await RequireDeniedAsync(request, $"UPDATE {rows.ObjectsTable} SET size = 0");
        await RequireDeniedAsync(request, "SET ROLE postgres");
        await RequireDeniedAsync(request,
            $"SELECT * FROM {PostgresInstanceStorageAdministrationContract.UsageFunctionSql(request)}()");
        await RequireDeniedAsync(request,
            $"SELECT * FROM {PostgresInstanceStorageAdministrationContract.ScopesFunctionSql(request)}()");
    }

    [Test]
    public async Task GlobalCapabilityIsColumnBoundNoLoginAndReturnsOnlyAccountingShapes()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();
        await AssertOrdinaryAsync(global);
        foreach (string table in new[] { rows.ObjectsTable, rows.CountersTable, rows.SessionsTable })
        {
            await RequireDeniedAsync(global, $"SELECT * FROM {table}");
            await RequireDeniedAsync(global, $"SELECT count(*) FROM {table}");
            await RequireDeniedAsync(global, $"DELETE FROM {table}");
        }
        await RequireDeniedAsync(global, $"SET ROLE {PostgresInstanceStorageAdministrationContract.OwnerRole}");
        await RequireDeniedAsync(global, $"SET ROLE {PostgresInstanceStorageAdministrationContract.MigratorRole}");
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            SELECT count(*)::integer FROM pg_catalog.pg_roles
            WHERE rolname IN ('{PostgresInstanceStorageAdministrationContract.OwnerRole}',
                '{PostgresInstanceStorageAdministrationContract.MigratorRole}',
                '{PostgresInstanceStorageAdministrationContract.GlobalRole}')
                AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
            """;
        await Assert.That((int)(await command.ExecuteScalarAsync())!).IsEqualTo(3);
        command.CommandText = $"""
            SELECT count(*)::integer FROM pg_catalog.pg_proc AS routine
            JOIN pg_catalog.pg_roles AS owner ON owner.oid = routine.proowner
            WHERE routine.oid IN (
                '{PostgresInstanceStorageAdministrationContract.UsageFunctionSql(global)}()'::regprocedure,
                '{PostgresInstanceStorageAdministrationContract.ScopesFunctionSql(global)}()'::regprocedure)
                AND routine.prosecdef AND routine.provolatile = 's'
                AND routine.proconfig @> ARRAY['search_path=pg_catalog']
                AND owner.rolname = '{PostgresInstanceStorageAdministrationContract.OwnerRole}'
            """;
        await Assert.That((int)(await command.ExecuteScalarAsync())!).IsEqualTo(2);
        command.CommandText = $"""
            SELECT has_table_privilege('{PostgresInstanceStorageAdministrationContract.OwnerRole}', '{rows.ObjectsTable}', 'SELECT'),
                has_column_privilege('{PostgresInstanceStorageAdministrationContract.OwnerRole}', '{rows.ObjectsTable}', 'tenant_id', 'SELECT'),
                has_column_privilege('{PostgresInstanceStorageAdministrationContract.OwnerRole}', '{rows.ObjectsTable}', 'object_key', 'SELECT'),
                has_table_privilege('{PostgresInstanceStorageAdministrationContract.OwnerRole}', '{rows.CountersTable}', 'UPDATE')
            """;
        await using (var grants = await command.ExecuteReaderAsync())
        {
            await Assert.That(await grants.ReadAsync()).IsTrue();
            await Assert.That(grants.GetBoolean(0)).IsFalse();
            await Assert.That(grants.GetBoolean(1)).IsTrue();
            await Assert.That(grants.GetBoolean(2)).IsFalse();
            await Assert.That(grants.GetBoolean(3)).IsFalse();
        }
        command.CommandText = $"SELECT * FROM {PostgresInstanceStorageAdministrationContract.UsageFunctionSql(global)}()";
        await using (var usage = await command.ExecuteReaderAsync())
        {
            await Assert.That(usage.FieldCount).IsEqualTo(6);
            await Assert.That(Enumerable.Range(0, usage.FieldCount).Select(usage.GetName)).IsEquivalentTo(
                ["Provider", "UsedBytes", "ReservedBytes", "QuarantinedBytes", "ObjectCount", "LastRecalculatedAt"]);
            await Assert.That(await usage.ReadAsync()).IsTrue();
            await Assert.That(usage.GetString(0)).IsEqualTo(StorageProviders.Local);
            await Assert.That(usage.GetInt64(1)).IsEqualTo(35);
            await Assert.That(await usage.ReadAsync()).IsFalse();
        }
        command.CommandText = $"SELECT * FROM {PostgresInstanceStorageAdministrationContract.ScopesFunctionSql(global)}()";
        await using var scopes = await command.ExecuteReaderAsync();
        await Assert.That(scopes.FieldCount).IsEqualTo(2);
        await Assert.That(scopes.GetName(0)).IsEqualTo("TenantId");
        await Assert.That(scopes.GetName(1)).IsEqualTo("Provider");
        var actual = new List<InstanceStorageAccountingScope>();
        while (await scopes.ReadAsync())
            actual.Add(new(scopes.GetGuid(0), scopes.GetString(1)));
        await Assert.That(actual).IsEquivalentTo(
        [
            new InstanceStorageAccountingScope(rows.A, StorageProviders.Local),
            new InstanceStorageAccountingScope(rows.B, StorageProviders.Local),
            new InstanceStorageAccountingScope(rows.B, StorageProviders.S3Compatible)
        ]);
    }

    [Test]
    public async Task UnentitledGlobalLoginCannotDiscoverScopesOrReportAccounting()
    {
        await using var rows = await StorageRows.CreateAsync(fixture, grantCapability: false);
        var administration = new InstanceStorageAdministration(rows.Global, new NoRequestAccountingOwners());
        var report = await Assert.ThrowsAsync<PostgresException>(
            async () => await administration.ReadUsageAsync(CancellationToken.None));
        await Assert.That(report!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var repair = await Assert.ThrowsAsync<PostgresException>(
            async () => await ExecuteNativeRepairAsync(rows.Global));
        await Assert.That(repair!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task LaterTenantFailureRollsBackEarlierTenantAndNewCounter()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var seed = fixture.CreateDbContext();
        var reservation = await seed.StorageUploadSessions.FirstAsync(row => row.TenantId == rows.A);
        seed.StorageUploadSessions.Add(StorageRows.Session(rows.A, reservation.StorageProviderBindingId!.Value,
            StorageProviders.S3Compatible, StorageUploadSessionStates.Reserved, 37));
        await seed.SaveChangesAsync();
        var before = await seed.StorageUsageCounters.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        string schema = seed.Model.FindEntityType(typeof(StorageUsageCounter))!.GetSchema()!;
        string function = $"\"{schema}\".\"storage_repair_failure\"";
        await seed.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql
            SECURITY DEFINER SET search_path = pg_catalog AS $body$
            BEGIN
                IF NEW.tenant_id = '{rows.B}'::uuid AND NEW.used_bytes <> OLD.used_bytes THEN
                    IF NOT EXISTS (SELECT 1 FROM {rows.CountersTable}
                        WHERE tenant_id = '{rows.A}'::uuid AND provider = '{StorageProviders.Local}' AND used_bytes = 317)
                       OR NOT EXISTS (SELECT 1 FROM {rows.CountersTable}
                        WHERE tenant_id = '{rows.A}'::uuid AND provider = '{StorageProviders.S3Compatible}' AND reserved_bytes = 37) THEN
                        RAISE EXCEPTION 'earlier repairs were not visible' USING ERRCODE = 'P0002';
                    END IF;
                    RAISE EXCEPTION 'later tenant repair failed' USING ERRCODE = 'P0001';
                END IF;
                RETURN NEW;
            END $body$;
            CREATE TRIGGER storage_repair_failure BEFORE UPDATE ON {rows.CountersTable}
                FOR EACH ROW EXECUTE FUNCTION {function}();
            """);
        try
        {
            var failure = await Assert.ThrowsAsync<Exception>(() =>
                ExecuteNativeRepairAsync(rows.Global))
                ?? throw new InvalidOperationException("Storage repair must fail at the injected database trigger.");
            var databaseFailure = failure as PostgresException ?? failure.InnerException as PostgresException;
            if (databaseFailure is null)
                throw new InvalidOperationException(
                    $"Storage repair did not reach the injected database failure: {failure.GetType().Name}: {failure.Message}",
                    failure);
            await Assert.That(databaseFailure.SqlState).IsEqualTo("P0001");
            await using var verify = fixture.CreateDbContext();
            var after = await verify.StorageUsageCounters.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            await Assert.That(after.Length).IsEqualTo(before.Length);
            for (int index = 0; index < before.Length; index++)
            {
                await Assert.That(after[index].UsedBytes).IsEqualTo(before[index].UsedBytes);
                await Assert.That(after[index].ReservedBytes).IsEqualTo(before[index].ReservedBytes);
                await Assert.That(after[index].ConcurrencyStamp).IsEqualTo(before[index].ConcurrencyStamp);
                await Assert.That(after[index].LastRecalculatedAt).IsEqualTo(before[index].LastRecalculatedAt);
                await Assert.That(after[index].UpdatedAt).IsEqualTo(before[index].UpdatedAt);
            }
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync($"""
                DROP TRIGGER storage_repair_failure ON {rows.CountersTable};
                DROP FUNCTION {function}();
                """);
        }
    }

    [Test]
    public async Task ExactTenantRepairRollsBackAllProviderWritesAndRejectsForeignScope()
    {
        await using var rows = await StorageRows.CreateAsync(fixture);
        await using var tenant = await rows.Runtime.CreateDbContextAsync(rows.B);
        var repository = new StorageUsageCounterRepository(tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.RecalculateForInstanceStorageAdministrationAsync(rows.A, StorageProviders.Local, Now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCoreUnitOfWork(tenant).ExecuteSerializableAsync<bool>(async ct =>
            {
                await repository.RecalculateForInstanceStorageAdministrationAsync(rows.B, StorageProviders.Local, Now, ct);
                await repository.RecalculateForInstanceStorageAdministrationAsync(rows.B, StorageProviders.S3Compatible, Now, ct);
                throw new InvalidOperationException("Injected failure after both provider writes.");
            }));
        var totals = await new InstanceStorageAdministration(rows.Global, new NoRequestAccountingOwners()).ReadUsageAsync(default);
        await Assert.That(totals.Count).IsEqualTo(1);
        await Assert.That(totals.Single().UsedBytes).IsEqualTo(35);
        await Assert.That(totals.Single().ReservedBytes).IsEqualTo(10);
        await Assert.That(totals.Single().ObjectCount).IsEqualTo(5);
    }

    private static async Task<IReadOnlyList<InstanceStorageProviderTotals>> ExecuteNativeRepairAsync(
        IGlobalExploreDbContextFactory contexts)
    {
        await using var context = await contexts.CreateDbContextAsync();
        return await ((ISerializableAttemptOwner)new EfCoreUnitOfWork(context)).ExecuteAsync(async token =>
            await context.Database.SqlQueryRaw<InstanceStorageProviderTotals>($"""
                SELECT "Provider" AS provider, "UsedBytes" AS used_bytes,
                    "ReservedBytes" AS reserved_bytes, "QuarantinedBytes" AS quarantined_bytes,
                    "ObjectCount" AS object_count, "LastRecalculatedAt" AS last_recalculated_at
                FROM {PostgresInstanceStorageAdministrationContract.RecalculateFunctionSql(context)}()
                """).ToListAsync(token));
    }

    private sealed class NoRequestAccountingOwners : IPolicyMutationExecutionScopeFactory
    {
        public ValueTask<IPolicyMutationExecutionScope> CreateAsync(
            PolicyMutationScope selection, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("Native database controls do not carry original request authority.");
    }

    private static async Task AssertOrdinaryAsync(ExploreDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                AND NOT rolcreaterole AND NOT rolreplication
            FROM pg_catalog.pg_roles WHERE rolname = current_user
            """;
        await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
    }

    private static async Task RequireDeniedAsync(ExploreDbContext context, string statement)
    {
        var denied = await Assert.ThrowsAsync<PostgresException>(
            async () => await context.Database.ExecuteSqlRawAsync(statement));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private sealed class StorageRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private readonly Guid[] _tenantIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() }.Order().ToArray();
        internal Guid A => _tenantIds[0];
        internal Guid B => _tenantIds[1];
        internal Guid HeldId { get; private set; }
        internal Guid HeldStamp { get; private set; }
        internal string HeldKey { get; private set; } = null!;
        internal string ObjectsTable { get; private set; } = null!;
        internal string CountersTable { get; private set; } = null!;
        internal string SessionsTable { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Global { get; private set; } = null!;
        internal TenantExploreDbContextFactory Runtime { get; private set; } = null!;
        private readonly string _runtimeRole = $"storage_runtime_{Guid.CreateVersion7():N}";
        private readonly string _globalRole = $"storage_global_{Guid.CreateVersion7():N}";
        private readonly List<string> _tables = [];
        private readonly List<string> _connections = [];
        private string _schema = null!;

        internal static async Task<StorageRows> CreateAsync(PostgreSqlContainerFixture fixture, bool grantCapability = true)
        {
            await using var tenants = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
            var rows = new StorageRows(fixture);
            // Reuse the migrated fixture's actual tenants; each storage test owns its generated logins.
            await using var seed = fixture.CreateDbContext();
            rows._schema = tenants.Schema;
            seed.Tenants.AddRange(Tenant(rows.A), Tenant(rows.B));
            var binding = StorageProviderBinding.Local(Path.Combine(
                Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), $"storage-{Guid.CreateVersion7():N}"));
            seed.StorageProviderBindings.Add(binding);
            await seed.SaveChangesAsync();
            var activeA = Object(rows.A, binding.Id, 101);
            var held = Object(rows.A, binding.Id, 107);
            held.IsDeleted = true;
            held.LifecycleState = StorageObjectLifecycleStates.DeleteRequested;
            rows.HeldId = held.Id;
            rows.HeldStamp = held.ConcurrencyStamp;
            rows.HeldKey = held.ObjectKey!;
            var retiring = Object(rows.A, binding.Id, 109);
            retiring.RequestDelete();
            var activeB = Object(rows.B, binding.Id, 103);
            var quarantine = Object(rows.B, binding.Id, 17);
            quarantine.MarkQuarantined(null, "Bounded native accounting", Now);
            var resource = Object(rows.B, binding.Id, 23);
            resource.Purpose = StorageObjectPurposes.EventResource;
            resource.OwningResourceKind = StorageOwningResourceKinds.EventResource;
            resource.OwningResourceId = Guid.CreateVersion7();
            var pending = Object(rows.B, binding.Id, 19);
            pending.Purpose = StorageObjectPurposes.EventResource;
            pending.OwningResourceKind = StorageOwningResourceKinds.EventResource;
            pending.OwningResourceId = Guid.CreateVersion7();
            pending.LifecycleState = StorageObjectLifecycleStates.Pending;
            var tombstoned = Object(rows.B, binding.Id, 313);
            var foreign = Object(rows.B, binding.Id, 509, StorageProviders.LegacyExternal);
            seed.StorageObjects.AddRange(activeA, held, retiring, activeB, quarantine, resource, pending, tombstoned, foreign);
            seed.StorageObjectDeletionTombstones.Add(StorageObjectDeletionTombstone.Create(
                tombstoned.Id, rows.B, StorageProviders.Local, binding.Id, tombstoned.ObjectKey!, null, true, Now));
            seed.StorageUploadSessions.AddRange(
                Session(rows.A, binding.Id, StorageProviders.Local, StorageUploadSessionStates.Reserved, 7),
                Session(rows.A, binding.Id, StorageProviders.Local, StorageUploadSessionStates.Uploading, 11),
                Session(rows.B, binding.Id, StorageProviders.Local, StorageUploadSessionStates.Reserved, 13),
                Session(rows.B, binding.Id, StorageProviders.Local, StorageUploadSessionStates.Failed, 701),
                Session(rows.B, binding.Id, StorageProviders.Local, StorageUploadSessionStates.Canceled, 709),
                Session(rows.B, binding.Id, StorageProviders.S3Compatible, StorageUploadSessionStates.Reserved, 31));
            seed.StorageUsageCounters.AddRange(
                Counter(rows.A, 12, 3, 5, 2), Counter(rows.B, 23, 7, 11, 3));
            await seed.SaveChangesAsync();
            rows.HeldStamp = held.ConcurrencyStamp;
            foreach (Type type in new[] { typeof(StorageObject), typeof(StorageUsageCounter),
                typeof(StorageUploadSession), typeof(StorageObjectDeletionTombstone) })
            {
                var entity = seed.Model.FindEntityType(type)!;
                rows._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            rows.ObjectsTable = rows._tables[0];
            rows.CountersTable = rows._tables[1];
            rows.SessionsTable = rows._tables[2];
            await PostgresInstanceStorageAdministrationContract.ApplyAsync(seed);
            string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var grant = admin.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{rows._runtimeRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
                CREATE ROLE "{rows._globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
                GRANT USAGE ON SCHEMA "{rows._schema}" TO "{rows._runtimeRole}", "{rows._globalRole}";
                GRANT SELECT ON {string.Join(", ", rows._tables)} TO "{rows._runtimeRole}";
                GRANT INSERT, UPDATE ON {rows.CountersTable} TO "{rows._runtimeRole}";
                """;
            if (grantCapability)
                grant.CommandText += $"\nGRANT {PostgresInstanceStorageAdministrationContract.GlobalRole} TO \"{rows._globalRole}\" WITH INHERIT TRUE, SET FALSE;";
            await grant.ExecuteNonQueryAsync();
            rows.Global = new GlobalExploreDbContextFactory(rows.Options(
                PrimaryDatabaseRole.Global, rows._globalRole, globalPassword), poolSize: 1);
            rows.Runtime = new TenantExploreDbContextFactory(rows.Options(
                PrimaryDatabaseRole.Runtime, rows._runtimeRole, runtimePassword), poolSize: 1);
            return rows;
        }

        private PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role, string login, string password)
        {
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = login,
                Password = password
            };
            _connections.Add(connection.ConnectionString);
            return new PrimaryDatabaseConnectionOptions
            {
                Role = role,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Schema = _schema,
                Username = login,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string connection in _connections)
            {
                using var pool = new NpgsqlConnection(connection);
                NpgsqlConnection.ClearPool(pool);
            }
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var revoke = admin.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{_runtimeRole}";
                REVOKE USAGE ON SCHEMA "{_schema}" FROM "{_runtimeRole}", "{_globalRole}";
                REVOKE {PostgresInstanceStorageAdministrationContract.GlobalRole} FROM "{_globalRole}";
                DROP ROLE "{_runtimeRole}";
                DROP ROLE "{_globalRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }

        private static Tenant Tenant(Guid id) => new()
        {
            Id = id,
            FullName = "Storage accounting",
            Slug = $"storage-{id:N}",
            TenantStatusId = (int)Explore.Domain.Enums.TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = Now
        };

        private static StorageObject Object(Guid tenant, Guid binding, long size,
            string provider = StorageProviders.Local) => new()
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                FileTypeId = (int)Explore.Domain.Enums.FileTypeEnum.Document,
                FileType = null!,
                StorageProviderBindingId = provider == StorageProviders.LegacyExternal ? null : binding,
                ObjectKey = provider == StorageProviders.LegacyExternal ? null : $"storage/{Guid.CreateVersion7():N}",
                Provider = provider,
                FullName = "private.txt",
                SafeDisplayName = "private.txt",
                Extension = ".txt",
                ContentType = "text/plain",
                Size = size,
                Visibility = StorageObjectVisibilities.PrivateOwner,
                Purpose = StorageObjectPurposes.Document,
                LifecycleState = StorageObjectLifecycleStates.Active,
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };

        internal static StorageUploadSession Session(Guid tenant, Guid binding, string provider, string state, long bytes) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Provider = provider,
            StorageProviderBindingId = binding,
            ReservedBytes = bytes,
            ExpectedSizeBytes = bytes,
            ContentType = "text/plain",
            SafeDisplayName = "private.txt",
            Purpose = StorageObjectPurposes.Document,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Status = state,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(1),
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        private static StorageUsageCounter Counter(Guid tenant, long used, long reserved, long quarantined, long count) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Provider = StorageProviders.Local,
            UsedBytes = used,
            ReservedBytes = reserved,
            QuarantinedBytes = quarantined,
            ObjectCount = count,
            CreatedAt = Now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }
}
