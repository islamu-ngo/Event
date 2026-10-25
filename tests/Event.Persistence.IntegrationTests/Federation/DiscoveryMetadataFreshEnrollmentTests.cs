using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

[NotInParallel]
public sealed class DiscoveryMetadataFreshEnrollmentTests
{
    private const string RlsProof = "Fresh metadata enrollment verifies PostgreSQL tenant equality independently of EF filtering.";

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task OriginalQuotedLoginInstallsAndReattestsOnlyItsDeclaredMetadataAuthority(PrimaryDatabaseRole quotedRole)
    {
        await using var fixture = new DiscoveryMetadataEnrollmentPostgresFixture(quotedRole);
        await fixture.InitializeAsync();
        await using var migrator = fixture.CreateMigrator();
        await migrator.Database.OpenConnectionAsync();
        var migrationConnection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        int migrationBackend = migrationConnection.ProcessID;
        await AssertIdentityAsync(migrator, fixture.Enrollment.MigratorLogin);

        // This first installation, not a renamed stale policy, must accept the
        // exact configured login names while retaining every installer precondition.
        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment);
        await Assert.That(migrator.Database.CurrentTransaction).IsNull();
        await AssertInventoryAsync(migrator, fixture.Enrollment);
        string installedImage = await AuthorityImageAsync(migrator);
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(await AuthorityImageAsync(migrator)).IsEqualTo(installedImage);
            await transaction.CommitAsync();
        });
        await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(migrationConnection);
        await Assert.That(migrationConnection.ProcessID).IsEqualTo(migrationBackend);

        await using var global = await fixture.CreateGlobalAsync();
        await global.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)global.Database.GetDbConnection();
        var contextId = global.ContextId;
        int backend = connection.ProcessID;
        await AssertIdentityAsync(global, fixture.Enrollment.GlobalLogin);
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default);
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        global.RequireBoundTenantLease();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.IsTenantFilterBypassed).IsFalse();
        await Assert.That(await global.Tenants.AsNoTracking().Select(row => row.Id).CountAsync()).IsEqualTo(2);
        await Assert.That(await global.Set<EventDiscoveryRevision>().IgnoreTenantFilter(RlsProof)
            .AsNoTracking().CountAsync()).IsEqualTo(2);

        await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        });

        await using var runtime = fixture.CreateRuntime(fixture.FirstTenant);
        await runtime.Database.OpenConnectionAsync();
        await AssertIdentityAsync(runtime, fixture.Enrollment.RuntimeLogin);
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(runtime, fixture.Enrollment, false, default);
        Guid[] visible = await runtime.Set<EventDiscoveryRevision>().IgnoreTenantFilter(RlsProof)
            .AsNoTracking().Select(row => row.TenantId).ToArrayAsync();
        await Assert.That(visible.Length).IsEqualTo(1);
        await Assert.That(visible.Single()).IsEqualTo(fixture.FirstTenant);
        int foreignWrites = await runtime.Set<EventDiscoveryRevision>().IgnoreTenantFilter(RlsProof)
            .Where(row => row.TenantId == fixture.SecondTenant)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.DisclosureEpoch, 99L));
        await Assert.That(foreignWrites).IsEqualTo(0);
        await Assert.That(await global.Set<EventDiscoveryRevision>().IgnoreTenantFilter(RlsProof)
            .Where(row => row.TenantId == fixture.SecondTenant).Select(row => row.DisclosureEpoch).SingleAsync()).IsEqualTo(11L);

        foreach (var caller in new[] { global, runtime })
        {
            var names = new HumanPrincipalSourceSql(caller);
            foreach (string sql in new[]
            {
                $"SELECT {names.Column(typeof(Tenant), nameof(Tenant.FullName))} FROM {names.Table(typeof(Tenant))}",
                $"SELECT {names.Column(typeof(User), nameof(User.Id))} FROM {names.Table(typeof(User))}",
                $"SELECT xmin FROM {names.Table(typeof(EventDiscoveryRevision))}",
                $"DELETE FROM {names.Table(typeof(EventDiscoveryRevision))} WHERE FALSE"
            })
            {
                await using var command = Command(caller, sql);
                var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        }

        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default);
        await Assert.That(global.ContextId).IsEqualTo(contextId);
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(await AuthorityImageAsync(migrator)).IsEqualTo(installedImage);
    }

    [Test]
    public async Task MetadataRetryAdmitsFixedMembershipUpdatesAndRejectsPrivilegeDrift()
    {
        await using var fixture = new DiscoveryMetadataEnrollmentPostgresFixture(PrimaryDatabaseRole.Global);
        await fixture.InitializeAsync();
        await using var migrator = fixture.CreateMigrator();
        await migrator.Database.OpenConnectionAsync();
        var migrationConnection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        var migrationContextId = migrator.ContextId;
        int migrationBackend = migrationConnection.ProcessID;
        await AssertIdentityAsync(migrator, fixture.Enrollment.MigratorLogin);

        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment);
        await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.Enrollment);
        await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment);

        await using var global = await fixture.CreateGlobalAsync();
        await global.Database.OpenConnectionAsync();
        var globalConnection = (NpgsqlConnection)global.Database.GetDbConnection();
        var globalContextId = global.ContextId;
        int globalBackend = globalConnection.ProcessID;
        await AssertIdentityAsync(global, fixture.Enrollment.GlobalLogin);
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default);
        await AssertCustodyAsync();

        var names = new HumanPrincipalSourceSql(migrator);
        string login = names.Quote(fixture.Enrollment.GlobalLogin);
        foreach (var change in new[]
        {
            (Type: typeof(Tenant), Property: nameof(Tenant.Slug), Remove: false, GrantOption: false),
            (Type: typeof(Tenant), Property: nameof(Tenant.TenantStatusId), Remove: false, GrantOption: true),
            (Type: typeof(EventDiscoveryRevision), Property: nameof(EventDiscoveryRevision.DisclosureEpoch), Remove: true, GrantOption: false)
        })
        {
            string table = names.Table(change.Type);
            string column = names.Column(change.Type, change.Property);
            string permission = $"UPDATE ({column}) ON {table}";
            string tamper = change.Remove ? $"REVOKE {permission} FROM {login}"
                : $"GRANT {permission} TO {login}" + (change.GrantOption ? " WITH GRANT OPTION" : "");
            string restore = change.Remove ? $"GRANT {permission} TO {login}"
                : change.GrantOption ? $"REVOKE GRANT OPTION FOR {permission} FROM {login}"
                : $"REVOKE {permission} FROM {login}";
            var failures = new List<Exception>();
            try
            {
                await using (var command = Command(migrator, tamper)) await command.ExecuteNonQueryAsync();
                var installer = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment));
                await Assert.That(installer!.Message == "discovery_metadata_permissions_drift").IsTrue();
                var current = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default));
                await Assert.That(current!.Message == "discovery_metadata_current_owner_not_ready").IsTrue();
                await using var retained = Command(migrator, """
                    SELECT pg_catalog.has_column_privilege(@login,@relation::regclass,@column,@privilege) = @expected
                        AND (NOT @grant_option OR pg_catalog.has_column_privilege(@login,@relation::regclass,@column,'UPDATE'))
                    """);
                retained.Parameters.Add(new NpgsqlParameter("login", NpgsqlTypes.NpgsqlDbType.Text) { Value = fixture.Enrollment.GlobalLogin });
                retained.Parameters.Add(new NpgsqlParameter("relation", NpgsqlTypes.NpgsqlDbType.Text) { Value = table });
                retained.Parameters.Add(new NpgsqlParameter("column", NpgsqlTypes.NpgsqlDbType.Text) { Value = names.ColumnName(change.Type, change.Property) });
                retained.Parameters.Add(new NpgsqlParameter("privilege", NpgsqlTypes.NpgsqlDbType.Text)
                    { Value = change.GrantOption ? "UPDATE WITH GRANT OPTION" : "UPDATE" });
                retained.Parameters.Add(new NpgsqlParameter("expected", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = !change.Remove });
                retained.Parameters.Add(new NpgsqlParameter("grant_option", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = change.GrantOption });
                await Assert.That(await retained.ExecuteScalarAsync() is true).IsTrue();
                await AssertCustodyAsync();
            }
            catch (Exception failure) { failures.Add(failure); }
            finally
            {
                try
                {
                    await using var command = Command(migrator, restore);
                    await command.ExecuteNonQueryAsync();
                    await using var restored = Command(migrator, """
                        SELECT pg_catalog.has_column_privilege(@login,@relation::regclass,@column,'UPDATE') = @expected
                            AND NOT pg_catalog.has_column_privilege(@login,@relation::regclass,@column,'UPDATE WITH GRANT OPTION')
                        """);
                    restored.Parameters.Add(new NpgsqlParameter("login", NpgsqlTypes.NpgsqlDbType.Text) { Value = fixture.Enrollment.GlobalLogin });
                    restored.Parameters.Add(new NpgsqlParameter("relation", NpgsqlTypes.NpgsqlDbType.Text) { Value = table });
                    restored.Parameters.Add(new NpgsqlParameter("column", NpgsqlTypes.NpgsqlDbType.Text) { Value = names.ColumnName(change.Type, change.Property) });
                    restored.Parameters.Add(new NpgsqlParameter("expected", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = change.Remove || change.GrantOption });
                    await Assert.That(await restored.ExecuteScalarAsync() is true).IsTrue();
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
                try
                {
                    await PostgresDiscoveryMetadataPermissions.ApplyAsync(migrator, fixture.Enrollment);
                    await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.Enrollment, true, default);
                    await AssertCustodyAsync();
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Metadata retry invariant and cleanup failures.", failures);
        }

        async Task AssertCustodyAsync()
        {
            migrator.RequireBoundTenantLease();
            global.RequireBoundTenantLease();
            bool migrationRetained = migrator.ContextId == migrationContextId
                && ReferenceEquals(migrator.Database.GetDbConnection(), migrationConnection)
                && migrationConnection.State == ConnectionState.Open && migrationConnection.ProcessID == migrationBackend
                && migrator.Database.CurrentTransaction is null && migrator.TenantFilterTenantId is null && !migrator.IsTenantFilterBypassed;
            bool globalRetained = global.ContextId == globalContextId
                && ReferenceEquals(global.Database.GetDbConnection(), globalConnection)
                && globalConnection.State == ConnectionState.Open && globalConnection.ProcessID == globalBackend
                && global.Database.CurrentTransaction is null && global.TenantFilterTenantId is null && !global.IsTenantFilterBypassed
                && System.Transactions.Transaction.Current is null;
            await Assert.That(migrationRetained).IsTrue();
            await Assert.That(globalRetained).IsTrue();
        }
    }

    private static async Task AssertIdentityAsync(ExploreDbContext context, string login)
    {
        await using var command = Command(context, "SELECT session_user=@login AND current_user=session_user AND current_setting('role') IN ('none',session_user::text)");
        command.Parameters.Add(new NpgsqlParameter("login", login));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task AssertInventoryAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var names = new HumanPrincipalSourceSql(context);
        string Fields<T>(string relation, params string[] properties) => string.Join(',', properties.Select(property =>
            $"('{relation}','{property}','{names.ColumnName(typeof(T), property)}')"));
        await using var command = Command(context, $"""
            WITH relations(label,id) AS (VALUES ('tenant',@tenant::regclass),('revision',@revision::regclass)),
            fields(label,property,column_name) AS (VALUES {Fields<Tenant>("tenant", "Id")},
                {Fields<EventDiscoveryRevision>("revision", "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch")}),
            logins(label,id) AS (SELECT CASE rolname WHEN @runtime THEN 'runtime' ELSE 'global' END,oid
                FROM pg_catalog.pg_roles WHERE rolname IN (@runtime,@global)),
            grants AS (
                SELECT l.label || ':' || r.label || ':' || COALESCE(f.property,a.attname) || ':' || acl.privilege_type
                    || CASE WHEN acl.is_grantable THEN ':grantable' ELSE '' END AS value
                FROM relations r JOIN pg_catalog.pg_attribute a ON a.attrelid=r.id
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl JOIN logins l ON l.id=acl.grantee
                LEFT JOIN fields f ON f.label=r.label AND f.column_name=a.attname
                UNION ALL
                SELECT l.label || ':' || r.label || ':TABLE:' || acl.privilege_type
                FROM relations r JOIN pg_catalog.pg_class c ON c.oid=r.id
                CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl JOIN logins l ON l.id=acl.grantee)
            SELECT value FROM grants ORDER BY value COLLATE "C"
            """);
        command.Parameters.Add(new NpgsqlParameter("tenant", names.Table(typeof(Tenant))));
        command.Parameters.Add(new NpgsqlParameter("revision", names.Table(typeof(EventDiscoveryRevision))));
        command.Parameters.Add(new NpgsqlParameter("runtime", enrollment.RuntimeLogin));
        command.Parameters.Add(new NpgsqlParameter("global", enrollment.GlobalLogin));
        var actual = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
        string[] expected = ["global:tenant:Id:SELECT", .. new[] { "runtime", "global" }.SelectMany(login =>
            new[] { "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:SELECT")
                .Concat(new[] { "Id", "TenantId", "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:INSERT"))
                .Concat(new[] { "IdentityEpoch", "DisclosureEpoch" }.Select(column => $"{login}:revision:{column}:UPDATE")))];
        await Assert.That(string.Join('|', actual)).IsEqualTo(string.Join('|', expected.Order(StringComparer.Ordinal)));
    }

    private static async Task<string> AuthorityImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = Command(context, """
            WITH relations AS (
                SELECT c.oid,c.relrowsecurity,c.relforcerowsecurity,c.relacl FROM pg_catalog.pg_class c
                WHERE c.oid IN (@tenant::regclass,@revision::regclass))
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_object(
                'relations',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY r.oid) FROM relations r),
                'columns',(SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attrelid,a.attnum,a.attacl) ORDER BY a.attrelid,a.attnum)
                    FROM pg_catalog.pg_attribute a JOIN relations r ON r.oid=a.attrelid),
                'policies',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(p) ||
                    pg_catalog.jsonb_build_object('comment',pg_catalog.obj_description(p.oid,'pg_policy')) ORDER BY p.oid)
                    FROM pg_catalog.pg_policy p JOIN relations r ON r.oid=p.polrelid))::text)
            """);
        command.Parameters.Add(new NpgsqlParameter("tenant", names.Table(typeof(Tenant))));
        command.Parameters.Add(new NpgsqlParameter("revision", names.Table(typeof(EventDiscoveryRevision))));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Metadata authority image is missing."));
    }

    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }
}
