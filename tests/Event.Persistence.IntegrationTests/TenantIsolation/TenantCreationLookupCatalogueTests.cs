using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Caching;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Exercises optional creator Role reads against the real Runtime-backed catalogue cache.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class TenantCreationLookupCatalogueTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeCatalogueAcceptsAbsentAndCompleteCreatorButRejectsPartialOrDelegatedRoleReads()
    {
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migrator);
        string roleTable = names.Table(typeof(Role));
        string globalLogin = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string roleId = names.Column(typeof(Role), nameof(Role.Id));
        string code = names.Column(typeof(Role), nameof(Role.MasterCode));
        string scope = names.Column(typeof(Role), nameof(Role.RoleScopeId));
        string description = names.Column(typeof(Role), nameof(Role.Description));
        string runtimeBefore = await RuntimeRoleGrantsAsync(migrator, roleTable);
        var expected = await migrator.Roles.AsNoTracking().ToListAsync();
        await Assert.That(expected.Count > 0).IsTrue();

        var runtimeFactory = new RuntimeFactory(fixture);
        await using var runtime = runtimeFactory.CreateDbContext();
        await runtime.Database.OpenConnectionAsync();
        var runtimeConnector = (NpgsqlConnection)runtime.Database.GetDbConnection();
        int runtimeBackend = runtimeConnector.ProcessID;
        var runtimeContext = runtime.ContextId;
        var globalFactory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await globalFactory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var globalConnector = (NpgsqlConnection)global.Database.GetDbConnection();
        int globalBackend = globalConnector.ProcessID;

        var services = new ServiceCollection();
        services.AddSingleton(fixture.NativeQueueEnrollment);
        services.AddSingleton<IDbContextFactory<ExploreDbContext>>(runtimeFactory);
        await using var provider = services.BuildServiceProvider();
        using var cache = new LookupDataCache(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<LookupDataCache>.Instance);

        // The class-local base fixture has the lookup contract but no creator package.
        await using (var absent = Command(migrator, """
            SELECT NOT pg_catalog.has_any_column_privilege(@global,CAST(@table AS regclass),'SELECT')
            """))
        {
            absent.Parameters.AddWithValue("global", fixture.NativeQueueEnrollment.GlobalLogin);
            absent.Parameters.AddWithValue("table", roleTable);
            await Assert.That(await absent.ExecuteScalarAsync()).IsEqualTo(true);
        }
        await RequireHealthyAsync(creatorInstalled: false);
        await PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        // This is the real startup failure: the same Runtime cache must still load
        // after the separately approved Global creator Role3 grants are installed.
        await RequireHealthyAsync(creatorInstalled: true);

        await AttackAsync($"REVOKE SELECT ({code}) ON {roleTable} FROM {globalLogin}",
            $"GRANT SELECT ({code}) ON {roleTable} TO {globalLogin}");
        await AttackAsync($"GRANT SELECT ({description}) ON {roleTable} TO {globalLogin}",
            $"REVOKE SELECT ({description}) ON {roleTable} FROM {globalLogin}");
        await AttackAsync($"GRANT SELECT ({roleId}) ON {roleTable} TO {globalLogin} WITH GRANT OPTION",
            $"REVOKE GRANT OPTION FOR SELECT ({roleId}) ON {roleTable} FROM {globalLogin}");
        await AttackAsync($"GRANT SELECT ({roleId}) ON {roleTable} TO PUBLIC",
            $"REVOKE SELECT ({roleId}) ON {roleTable} FROM PUBLIC");
        await AttackAsync($"GRANT SELECT ON {roleTable} TO {globalLogin}",
            $"REVOKE SELECT ON {roleTable} FROM {globalLogin}", restoreOriginalRoleReads: true);

        string delegated = names.Quote("creator_lookup_" + Guid.CreateVersion7().ToString("N"));
        await AttackAsync($"""
            CREATE ROLE {delegated} NOLOGIN NOINHERIT;
            GRANT SELECT ({roleId}) ON {roleTable} TO {delegated};
            GRANT {delegated} TO {globalLogin} WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;
            """, $"""
            REVOKE {delegated} FROM {globalLogin};
            REVOKE SELECT ({roleId}) ON {roleTable} FROM {delegated};
            DROP ROLE {delegated};
            """);

        await Assert.That(await RuntimeRoleGrantsAsync(migrator, roleTable)).IsEqualTo(runtimeBefore);
        runtime.RequireBoundTenantLease();
        await Assert.That(runtime.ContextId).IsEqualTo(runtimeContext);
        await Assert.That(runtime.TenantFilterTenantId).IsNull();
        await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
        await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(runtimeConnector);
        await Assert.That(runtimeConnector.ProcessID).IsEqualTo(runtimeBackend);
        await Assert.That(runtime.Database.CurrentTransaction).IsNull();
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnector);
        await Assert.That(globalConnector.ProcessID).IsEqualTo(globalBackend);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();

        async Task RequireHealthyAsync(bool creatorInstalled)
        {
            await cache.RefreshAsync();
            await RequireCachedRowsAsync();
            await PostgresLookupCatalogueReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment, default);
            await PostgresLookupCatalogueReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
            if (creatorInstalled)
                await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            await Assert.That(await RuntimeRoleGrantsAsync(migrator, roleTable)).IsEqualTo(runtimeBefore);
        }

        async Task RequireCachedRowsAsync()
        {
            await Assert.That(cache.Get<Role>().Count).IsEqualTo(expected.Count);
            foreach (var role in expected)
            {
                var cached = cache.GetById<Role>(role.Id);
                await Assert.That(cached).IsNotNull();
                await Assert.That(cached!.MasterCode).IsEqualTo(role.MasterCode);
                await Assert.That(cached.FullName).IsEqualTo(role.FullName);
                await Assert.That(cached.Description).IsEqualTo(role.Description);
                await Assert.That(cached.RoleScopeId).IsEqualTo(role.RoleScopeId);
                await Assert.That(cached.IsSystem).IsEqualTo(role.IsSystem);
            }
        }

        async Task AttackAsync(string mutation, string inverse, bool restoreOriginalRoleReads = false)
        {
            await ChangeAsync(migrator, mutation);
            try
            {
                string changed = await RoleAuthorityImageAsync(migrator, roleTable);
                var cacheDenied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cache.RefreshAsync());
                await Assert.That(cacheDenied.Message).IsEqualTo("global_lookup_read_permissions_not_ready");
                await RequireCachedRowsAsync();
                var readiness = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresLookupCatalogueReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment, default));
                await Assert.That(readiness.Message).IsEqualTo("global_lookup_read_permissions_not_ready");
                var installation = await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresLookupCatalogueReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
                await Assert.That(installation.SqlState).IsEqualTo("55000");
                await Assert.That(installation.MessageText).IsEqualTo("global_lookup_read_permissions_drift");
                await Assert.That(await RoleAuthorityImageAsync(migrator, roleTable)).IsEqualTo(changed);
                await Assert.That(await RuntimeRoleGrantsAsync(migrator, roleTable)).IsEqualTo(runtimeBefore);
            }
            finally
            {
                if (!restoreOriginalRoleReads)
                    await ChangeAsync(migrator, inverse);
                else
                {
                    await using var cleanup = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable, CancellationToken.None);
                    await using (var revoke = Command(migrator, inverse))
                        await revoke.ExecuteNonQueryAsync(CancellationToken.None);
                    // Prove the native table-REVOKE effect before restoring only
                    // this test's original fixed three-column creator grant.
                    await using (var missing = Command(migrator, """
                        SELECT count(*)=3 AND NOT bool_or(EXISTS (
                            SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                            WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@global)
                                AND acl.privilege_type='SELECT'))
                        FROM pg_catalog.pg_attribute a WHERE a.attrelid=CAST(@table AS regclass)
                            AND a.attname=ANY(@columns) AND a.attnum>0 AND NOT a.attisdropped
                        """))
                    {
                        missing.Parameters.AddWithValue("global", fixture.NativeQueueEnrollment.GlobalLogin);
                        missing.Parameters.AddWithValue("table", roleTable);
                        missing.Parameters.AddWithValue("columns", new[]
                        {
                            names.ColumnName(typeof(Role), nameof(Role.Id)),
                            names.ColumnName(typeof(Role), nameof(Role.MasterCode)),
                            names.ColumnName(typeof(Role), nameof(Role.RoleScopeId))
                        });
                        await Assert.That(await missing.ExecuteScalarAsync()).IsEqualTo(true);
                    }
                    await using (var restore = Command(migrator,
                        $"GRANT SELECT ({roleId},{code},{scope}) ON {roleTable} TO {globalLogin}"))
                        await restore.ExecuteNonQueryAsync(CancellationToken.None);
                    await cleanup.CommitAsync(CancellationToken.None);
                }
            }
            await RequireHealthyAsync(creatorInstalled: true);
        }
    }

    private async Task<string> RuntimeRoleGrantsAsync(ExploreDbContext context, string table)
    {
        await using var command = Command(context, """
            WITH grants AS (
                SELECT a.attnum,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=CAST(@table AS regclass) AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@runtime)
                UNION ALL
                SELECT 0,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_class c CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                WHERE c.oid=CAST(@table AS regclass) AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@runtime))
            SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(g) ORDER BY g.attnum,g.privilege_type)::text,'[]')) FROM grants g
            """);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("runtime", fixture.NativeQueueEnrollment.RuntimeLogin);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Runtime Role grants are missing."));
    }

    private async Task<string> RoleAuthorityImageAsync(ExploreDbContext context, string table)
    {
        await using var command = Command(context, """
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_object(
                'table',(SELECT pg_catalog.jsonb_build_array(c.oid,c.relowner,c.relacl,c.relrowsecurity,c.relforcerowsecurity)
                    FROM pg_catalog.pg_class c WHERE c.oid=CAST(@table AS regclass)),
                'columns',(SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attnum,a.attacl) ORDER BY a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=CAST(@table AS regclass)),
                'memberships',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(m) ORDER BY m.roleid)
                    FROM pg_catalog.pg_auth_members m WHERE m.member=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@global)))::text)
            """);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("global", fixture.NativeQueueEnrollment.GlobalLogin);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Role authority image is missing."));
    }

    private ExploreDbContext OpenMigrator()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private static async Task ChangeAsync(ExploreDbContext context, string sql)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, CancellationToken.None);
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private sealed class RuntimeFactory(PostgreSqlContainerFixture source) : IDbContextFactory<ExploreDbContext>
    {
        public ExploreDbContext CreateDbContext()
        {
            var context = source.CreateEnrolledTenantlessDbContext();
            context.BindTenantLease();
            return context;
        }

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
