using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ActorOwnershipReadPermissionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeGetsOnlyFixedOwnershipReadsAlongsideExistingMembershipRights()
    {
        await using var migration = OpenMigrator();
        await PostgresActorOwnershipReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            Guid tenantId = Guid.CreateVersion7();
            await using var runtime = OpenMigrator(new SelectedTenant(tenantId));
            await using var transaction = await runtime.Database.BeginTransactionAsync();
            var mapped = new HumanPrincipalSourceSql(runtime);
            string baseline = string.Join(',', new[] { nameof(TenantUser.Id), nameof(TenantUser.TenantId),
            nameof(TenantUser.UserId), nameof(TenantUser.IsDeleted), nameof(TenantUser.StatusId) }
                .Select(property => mapped.Column(typeof(TenantUser), property)));
            await using (var change = new NpgsqlCommand($"""
            GRANT SELECT ({baseline}) ON {mapped.Table(typeof(TenantUser))} TO {mapped.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)};
            SET LOCAL SESSION AUTHORIZATION {mapped.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)};
            """, (NpgsqlConnection)runtime.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction()))
                await change.ExecuteNonQueryAsync();
            await runtime.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_catalog.set_config('app.current_tenant_id', {tenantId.ToString("D")}, true)");
            await PostgresActorOwnershipReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
            var names = new HumanPrincipalSourceSql(runtime);
            foreach (var (type, properties) in new[]
            {
            (typeof(Actor), new[] { "Id", "UserId", "GroupId", "OrganizationId", "IsDeleted" }),
            (typeof(TenantUser), new[] { "Id", "TenantId", "UserId", "IsDeleted", "StatusId", "ActorId" })
        })
            {
                await using var command = runtime.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = """
                SELECT NOT pg_catalog.has_table_privilege(current_user,pg_catalog.to_regclass(@table),'SELECT')
                    AND NOT EXISTS (SELECT 1 FROM unnest(@columns::text[]) expected(name)
                        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attname=expected.name
                                AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=current_user)
                                AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@migrator)
                                AND acl.privilege_type='SELECT' AND NOT acl.is_grantable))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT') AND NOT a.attname=ANY(@columns::text[]))))
                """;
                command.Parameters.Add(new NpgsqlParameter("table", names.Table(type)));
                command.Parameters.Add(new NpgsqlParameter("columns", properties.Select(property => names.ColumnName(type, property)).ToArray()));
                command.Parameters.Add(new NpgsqlParameter("migrator", fixture.NativeQueueEnrollment.MigratorLogin));
                await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
            }

            var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Actors
                .Where(actor => actor.Id == Guid.Empty).ExecuteUpdateAsync(update => update.SetProperty(actor => actor.UserId, (Guid?)null)));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments("private")]
    [Arguments("write")]
    [Arguments("table")]
    [Arguments("grant-option")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin-only")]
    [Arguments("system-column")]
    [Arguments("partial")]
    [Arguments("actor-rls")]
    [Arguments("tenant-policy")]
    public async Task DriftRejectsReadAndReinstallWithoutRepair(string attack)
    {
        await using var strategy = OpenMigrator();
        await PostgresActorOwnershipReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            Guid tenantId = Guid.CreateVersion7();
            await using (var healthy = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId)))
            {
                healthy.BindTenantLease();
                await healthy.Database.OpenConnectionAsync();
                await PostgresActorOwnershipReadPermissions.RequireReadyAsync(healthy, fixture.NativeQueueEnrollment);
            }
            await using var context = OpenMigrator(new SelectedTenant(tenantId));
            await using var transaction = await context.Database.BeginTransactionAsync();
            var names = new HumanPrincipalSourceSql(context);
            string table = names.Table(typeof(Actor));
            string column = names.Column(typeof(Actor), nameof(Actor.UserId));
            string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            string other = names.Quote("actor_read_drift_" + Guid.CreateVersion7().ToString("N"));
            await transaction.CreateSavepointAsync("before_runtime_control");
            await using (var change = new NpgsqlCommand($"SET LOCAL SESSION AUTHORIZATION {runtime}",
                (NpgsqlConnection)context.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction()))
                await change.ExecuteNonQueryAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_catalog.set_config('app.current_tenant_id', {tenantId.ToString("D")}, true)");
            await PostgresActorOwnershipReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
            await transaction.RollbackToSavepointAsync("before_runtime_control");
            string sql = attack switch
            {
                "private" => $"GRANT SELECT ({names.Column(typeof(Actor), nameof(Actor.Description))}) ON {table} TO {runtime}",
                "write" => $"GRANT UPDATE ({column}) ON {table} TO {runtime}",
                "table" => $"GRANT SELECT ON {table} TO {runtime}",
                "grant-option" => $"GRANT SELECT ({column}) ON {table} TO {runtime} WITH GRANT OPTION",
                "public" => $"GRANT SELECT ({column}) ON {table} TO PUBLIC",
                "system-column" => $"GRANT SELECT (xmin) ON {table} TO {runtime}",
                "partial" => $"REVOKE SELECT ({column}) ON {table} FROM {runtime}",
                "actor-rls" => $"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY",
                "tenant-policy" => $"CREATE POLICY {other} ON {names.Table(typeof(TenantUser))} FOR SELECT TO {runtime} USING (true)",
                "inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "set-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(attack))
            };
            await context.Database.ExecuteSqlRawAsync(sql);
            string before = await CatalogueImageAsync(context);
            await transaction.CreateSavepointAsync("before_runtime_read");
            await using (var change = new NpgsqlCommand($"SET LOCAL SESSION AUTHORIZATION {runtime}",
                (NpgsqlConnection)context.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction()))
                await change.ExecuteNonQueryAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_catalog.set_config('app.current_tenant_id', {tenantId.ToString("D")}, true)");
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresActorOwnershipReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied?.Message).IsEqualTo("actor_ownership_read_permissions_drift");
            await transaction.RollbackToSavepointAsync("before_runtime_read");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresActorOwnershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresActorOwnershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReapplicationPreservesOriginalCallerTransactionAndCatalogue()
    {
        await using var strategy = OpenMigrator();
        await PostgresActorOwnershipReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var connection = context.Database.GetDbConnection();
            var physical = transaction.GetDbTransaction();
            int backend = ((NpgsqlConnection)connection).ProcessID;
            string before = await CatalogueImageAsync(context);
            await PostgresActorOwnershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresActorOwnershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), physical)).IsTrue();
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReadinessRequiresConfiguredRuntimeAndExplicitEnrollment()
    {
        await using var migration = OpenMigrator();
        await PostgresActorOwnershipReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7()));
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await PostgresActorOwnershipReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresActorOwnershipReadPermissions.RequireReadyAsync(runtime, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresActorOwnershipReadPermissions.ApplyAsync(runtime, fixture.NativeQueueEnrollment));
        await using var wrong = OpenMigrator(new SelectedTenant(Guid.CreateVersion7()));
        await wrong.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresActorOwnershipReadPermissions.RequireReadyAsync(wrong, fixture.NativeQueueEnrollment));
    }

    private ExploreDbContext OpenMigrator(ITenantContext? tenant = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant };
        context.BindTenantLease();
        return context;
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object(
                'relations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_catalog.pg_class t
                    WHERE t.oid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'columns',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'policies',(SELECT jsonb_agg(to_jsonb(p) ORDER BY p.oid) FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'memberships',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member,m.grantor) FROM pg_catalog.pg_auth_members m))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("tables", new[] { names.Table(typeof(Actor)), names.Table(typeof(TenantUser)) }));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
