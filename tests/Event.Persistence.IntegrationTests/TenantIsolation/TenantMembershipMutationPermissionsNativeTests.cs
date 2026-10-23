using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class TenantMembershipMutationPermissionsNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task FixedProfilePreservesOriginalTransactionAndPrivateColumnDenials()
    {
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        string installed = await ImageAsync(migrator);
        await using (var transaction = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
            int backend = connection.ProcessID;
            var physical = transaction.GetDbTransaction();
            await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(await ImageAsync(migrator)).IsEqualTo(installed);
            await transaction.RollbackAsync();
        }
        var runtimeFactory = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await using var runtime = await runtimeFactory.CreateDbContextAsync(Guid.CreateVersion7());
        await runtime.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(runtime, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(runtime);
        var sql = runtime.GetService<ISqlGenerationHelper>();
        string column = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.Id)));
        await using (var read = Command(runtime, $"SELECT {column} FROM {names.Table(typeof(TenantUser))} LIMIT 0"))
        await using (await read.ExecuteReaderAsync()) { }
        await Denied(runtime, $"SELECT * FROM {names.Table(typeof(TenantUserProfile))} LIMIT 0");
        await Denied(runtime, $"SELECT xmin FROM {names.Table(typeof(TenantUser))} LIMIT 0");
        await Denied(runtime, $"SELECT * FROM {names.Table(typeof(UserPii))} LIMIT 0");

        var globalFactory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await globalFactory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await transaction.RollbackAsync();
        }
        string tenantId = sql.DelimitIdentifier(names.ColumnName(typeof(Tenant), nameof(Tenant.Id)));
        await Denied(global, $"UPDATE {names.Table(typeof(Tenant))} SET {tenantId}={tenantId} WHERE false");
        await Assert.That(await ImageAsync(migrator)).IsEqualTo(installed);
    }

    [Test]
    [Arguments("private_column")]
    [Arguments("global_identity_update")]
    [Arguments("delete_delegation")]
    [Arguments("public_column")]
    [Arguments("missing_update")]
    [Arguments("system_column")]
    [Arguments("unforced_rls")]
    [Arguments("borrow_global")]
    [Arguments("policy_visibility")]
    public async Task AlteredProfileIsRejectedWithoutRepair(string drift)
    {
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        string before = await ImageAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        var sql = migrator.GetService<ISqlGenerationHelper>();
        string runtime = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string global = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        string membership = names.Table(typeof(TenantUser));
        string profile = names.Table(typeof(TenantUserProfile));
        string tenant = names.Table(typeof(Tenant));
        string memberId = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.Id)));
        string tenantId = sql.DelimitIdentifier(names.ColumnName(typeof(Tenant), nameof(Tenant.Id)));
        string profileId = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUserProfile), nameof(TenantUserProfile.Id)));
        string status = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.StatusId)));
        string selectedTenant = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.TenantId)));
        string tenantPredicate = $"{selectedTenant}=NULLIF(current_setting('app.current_tenant_id',true),'')::uuid";
        (string attack, string restore) = drift switch
        {
            "private_column" => ($"GRANT SELECT ({profileId}) ON {profile} TO {runtime}", $"REVOKE SELECT ({profileId}) ON {profile} FROM {runtime}"),
            "global_identity_update" => ($"GRANT UPDATE ({tenantId}) ON {tenant} TO {global}", $"REVOKE UPDATE ({tenantId}) ON {tenant} FROM {global}"),
            "delete_delegation" => ($"GRANT DELETE ON {profile} TO {runtime} WITH GRANT OPTION", $"REVOKE GRANT OPTION FOR DELETE ON {profile} FROM {runtime}"),
            "public_column" => ($"GRANT SELECT ({memberId}) ON {membership} TO PUBLIC", $"REVOKE SELECT ({memberId}) ON {membership} FROM PUBLIC"),
            "missing_update" => ($"REVOKE UPDATE ({status}) ON {membership} FROM {runtime}", $"GRANT UPDATE ({status}) ON {membership} TO {runtime}"),
            "system_column" => ($"GRANT SELECT (xmin) ON {membership} TO {runtime}", $"REVOKE SELECT (xmin) ON {membership} FROM {runtime}"),
            "unforced_rls" => ($"ALTER TABLE {membership} NO FORCE ROW LEVEL SECURITY", $"ALTER TABLE {membership} FORCE ROW LEVEL SECURITY"),
            "borrow_global" => ($"GRANT {global} TO {runtime} WITH INHERIT FALSE, SET FALSE", $"REVOKE {global} FROM {runtime}"),
            "policy_visibility" => ($"ALTER POLICY tenant_isolation ON {membership} USING (true) WITH CHECK (true)",
                $"ALTER POLICY tenant_isolation ON {membership} USING ({tenantPredicate}) WITH CHECK ({tenantPredicate})"),
            _ => throw new InvalidOperationException("Unknown fixed mutation.")
        };
        var factory = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await using var reader = await factory.CreateDbContextAsync(Guid.CreateVersion7());
        await reader.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(reader, fixture.NativeQueueEnrollment);
        await using (var command = Command(migrator, attack)) await command.ExecuteNonQueryAsync();
        try
        {
            string attacked = await ImageAsync(migrator);
            var rejected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(reader, fixture.NativeQueueEnrollment));
            await Assert.That(rejected.Message).IsEqualTo("tenant_membership_mutation_permissions_not_ready");
            var installer = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(installer.Message).IsEqualTo("tenant_membership_mutation_permissions_drift");
            await Assert.That(await ImageAsync(migrator)).IsEqualTo(attacked);
        }
        finally
        {
            await using var command = Command(migrator, restore);
            await command.ExecuteNonQueryAsync();
        }
        await Assert.That(await ImageAsync(migrator)).IsEqualTo(before);
        await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task Denied(ExploreDbContext context, string sql)
    {
        var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = Command(context, sql);
            await command.ExecuteNonQueryAsync();
        });
        await Assert.That(rejected.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) =>
        new(sql, (NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private static async Task<string> ImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = Command(context, """
            SELECT jsonb_agg(row_image ORDER BY relation)::text
            FROM (SELECT c.oid::regclass::text relation,
                jsonb_build_array(c.oid::regclass::text,c.relrowsecurity,c.relforcerowsecurity,
                    (SELECT jsonb_agg(jsonb_build_array(p.polname,p.polcmd,p.polpermissive,p.polroles,
                        pg_catalog.pg_get_expr(p.polqual,p.polrelid),pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid))
                        ORDER BY p.polname) FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid),
                    COALESCE((SELECT jsonb_agg(jsonb_build_array(acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                        ORDER BY acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                        FROM pg_catalog.aclexplode(c.relacl) acl),'[]'::jsonb),
                    (SELECT jsonb_agg(jsonb_build_array(a.attnum,COALESCE(
                        (SELECT jsonb_agg(jsonb_build_array(acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                            ORDER BY acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                            FROM pg_catalog.aclexplode(a.attacl) acl),'[]'::jsonb)) ORDER BY a.attnum)
                        FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum<>0 AND NOT a.attisdropped)) row_image
                FROM pg_catalog.pg_class c WHERE c.oid=ANY(@relations::regclass[])) image
            """);
        command.Parameters.AddWithValue("relations", new[] { names.Table(typeof(TenantUser)),
            names.Table(typeof(TenantUserProfile)), names.Table(typeof(TenantUserRoleGrant)), names.Table(typeof(Tenant)) });
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
