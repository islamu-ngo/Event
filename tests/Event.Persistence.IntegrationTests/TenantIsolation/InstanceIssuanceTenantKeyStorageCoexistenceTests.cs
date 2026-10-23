using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class InstanceIssuanceTenantKeyStorageCoexistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task InstanceIssuanceDormantLookupPoliciesPreserveTenantKeyAndSupportReadiness()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync(token);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue();
        await PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token);
        string originalRuntimeAcl = await RuntimeColumnAclAsync(migrator, token);

        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            PostgresInstanceApiKeyIssuanceContract.ApplyAsync(migrator, token));

        await AssertDormantIssuancePoliciesAsync(migrator, token);
        await Assert.That(await RuntimeColumnAclAsync(migrator, token)).IsEqualTo(originalRuntimeAcl);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue()
            .Because("owner-only dormant issuance policies must not invalidate the existing fixed Runtime key manifest");
        await PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token);

        Guid tenantId = Guid.CreateVersion7();
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owner = OpenMigrator();
            await using var humanGate = new HumanSecuritySourceGate(owner);
            await using var human = await humanGate.AcquireAsync(token);
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(owner);
            await using var discovery = await discoveryGate.AcquireAsync(token);
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
            int backend = connection.ProcessID;
            owner.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Issuance storage coexistence",
                Slug = $"issuance-storage-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await owner.SaveChangesAsync(token);
            await Assert.That(await owner.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, token)).IsTrue();
            await Assert.That(owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync(CancellationToken.None);
        });
        await Assert.That(await migrator.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, token)).IsFalse();
        await Assert.That(await RuntimeColumnAclAsync(migrator, token)).IsEqualTo(originalRuntimeAcl);
    }

    [Test]
    public async Task EnabledLookupRlsIsRejectedWithoutRepair()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync(token);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue();
        string table = new HumanPrincipalSourceSql(migrator).Table(typeof(ExternalApiKeyStatus));
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", token);
            await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsFalse();
            await transaction.CreateSavepointAsync("before_support_readiness", token);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(rejected.SqlState).IsEqualTo("55000");
            await Assert.That(rejected.MessageText).IsEqualTo("support_administration_ambient_scalar_authority");
            await transaction.RollbackToSavepointAsync("before_support_readiness", CancellationToken.None);
            await using var unchanged = Command(migrator,
                "SELECT relrowsecurity AND NOT relforcerowsecurity FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)");
            unchanged.Parameters.AddWithValue("table", table);
            await Assert.That(await unchanged.ExecuteScalarAsync(token)).IsEqualTo(true);
            await transaction.RollbackAsync(CancellationToken.None);
        });
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue();
    }

    [Test]
    [Arguments("runtime")]
    [Arguments("member")]
    [Arguments("public")]
    public async Task StatusLookupDormantPoliciesMustBeUnreachableFromRuntime(string audience)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync(token);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue();
        await PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(ExternalApiKeyStatus));
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        string member = "status_policy_member_" + Guid.CreateVersion7().ToString("N");
        string role = audience switch
        {
            "runtime" => names.Quote(runtime),
            "member" => names.Quote(member),
            _ => "PUBLIC"
        };
        await using (var relationship = Command(migrator, """
            SELECT NOT c.relrowsecurity AND NOT c.relforcerowsecurity
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)
            """))
        {
            relationship.Parameters.AddWithValue("table", table);
            await Assert.That(await relationship.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
        string policyName = "status_audience_" + Guid.CreateVersion7().ToString("N");
        string originalRuntimeAcl = await RuntimeColumnAclAsync(migrator, token);
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
            int backend = connection.ProcessID;
            if (audience == "member")
            {
                await migrator.Database.ExecuteSqlRawAsync(
                    $"CREATE ROLE {names.Quote(member)} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS", token);
                await migrator.Database.ExecuteSqlRawAsync(
                    $"GRANT {names.Quote(member)} TO {names.Quote(runtime)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE", token);
                await using var relationship = Command(migrator, """
                    SELECT pg_catalog.pg_has_role(@runtime,@member,'MEMBER')
                        AND NOT pg_catalog.pg_has_role(@runtime,@member,'USAGE')
                        AND NOT pg_catalog.has_table_privilege(@member,@table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        AND NOT pg_catalog.has_any_column_privilege(@member,@table,'SELECT,INSERT,UPDATE,REFERENCES')
                        AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                            WHERE roleid=pg_catalog.to_regrole(@member) AND member=pg_catalog.to_regrole(@runtime)
                                AND NOT inherit_option AND NOT set_option AND NOT admin_option)
                    """);
                relationship.Parameters.AddWithValue("runtime", runtime);
                relationship.Parameters.AddWithValue("member", member);
                relationship.Parameters.AddWithValue("table", table);
                await Assert.That(await relationship.ExecuteScalarAsync(token)).IsEqualTo(true);
                await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue()
                    .Because("membership without source privileges must remain healthy until the reachable policy is introduced");
                await PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token);
            }
            await migrator.Database.ExecuteSqlRawAsync(
                $"CREATE POLICY {names.Quote(policyName)} ON {table} FOR SELECT TO {role} USING (true)", token);
            string? policyImage = await PolicyImageAsync(migrator, table, policyName, token);
            await Assert.That(policyImage).IsNotNull();
            await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsFalse();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantApiKeyStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await transaction.CreateSavepointAsync("before_policy_readiness", token);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(rejected.SqlState).IsEqualTo("55000");
            await Assert.That(rejected.MessageText).IsEqualTo("support_administration_ambient_scalar_authority");
            await transaction.RollbackToSavepointAsync("before_policy_readiness", CancellationToken.None);
            await Assert.That(await PolicyImageAsync(migrator, table, policyName, token)).IsEqualTo(policyImage);
            await Assert.That(await RuntimeColumnAclAsync(migrator, token)).IsEqualTo(originalRuntimeAcl);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync(CancellationToken.None);
        });
        await Assert.That(await PolicyImageAsync(migrator, table, policyName, token)).IsNull();
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsTrue();
        await PostgresSupportAdministrationContract.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment, token);
        await Assert.That(await RuntimeColumnAclAsync(migrator, token)).IsEqualTo(originalRuntimeAcl);
    }

    private static async Task<string?> PolicyImageAsync(ExploreDbContext context, string table,
        string policyName, CancellationToken token)
    {
        await using var command = Command(context, """
            SELECT jsonb_build_array(c.relrowsecurity,c.relforcerowsecurity,p.polname,p.polcmd,
                p.polpermissive,p.polroles::text,p.polqual::text,p.polwithcheck::text)::text
            FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid=p.polrelid
            WHERE p.polrelid=pg_catalog.to_regclass(@table) AND p.polname=@policy
            """);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("policy", policyName);
        return await command.ExecuteScalarAsync(token) as string;
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<bool> CompleteKeyManifestAsync(ExploreDbContext context, CancellationToken token)
    {
        await using var command = Command(context,
            $"SELECT COALESCE(({PostgresTenantApiKeyStoragePermissions.CompleteManifest(context, fixture.NativeQueueEnrollment)}),false)");
        return await command.ExecuteScalarAsync(token) is true;
    }

    private async Task<string> RuntimeColumnAclAsync(ExploreDbContext context, CancellationToken token)
    {
        await using var command = Command(context, """
            SELECT COALESCE(jsonb_agg(jsonb_build_array(c.relname,a.attname,acl.grantor,acl.privilege_type,acl.is_grantable)
                ORDER BY c.relname,a.attname,acl.grantor,acl.privilege_type,acl.is_grantable)::text,'[]')
            FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid
            CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE n.nspname=@schema AND a.attnum<>0 AND NOT a.attisdropped
                AND acl.grantee=pg_catalog.to_regrole(@runtime)
            """);
        command.Parameters.AddWithValue("schema", new HumanPrincipalSourceSql(context).Schema);
        command.Parameters.AddWithValue("runtime", fixture.NativeQueueEnrollment.RuntimeLogin);
        return (string)(await command.ExecuteScalarAsync(token))!;
    }

    private static async Task AssertDormantIssuancePoliciesAsync(ExploreDbContext context, CancellationToken token)
    {
        string table = new HumanPrincipalSourceSql(context).Table(typeof(ExternalApiKeyStatus));
        await using var flags = Command(context,
            "SELECT NOT relrowsecurity AND NOT relforcerowsecurity FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)");
        flags.Parameters.AddWithValue("table", table);
        await Assert.That(await flags.ExecuteScalarAsync(token)).IsEqualTo(true);
        await using var policies = Command(context, """
            SELECT count(*)=2 AND bool_and(p.polpermissive
                AND p.polroles=ARRAY[pg_catalog.to_regrole(@owner)::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true'
                AND ((p.polname=@read AND p.polcmd='r' AND p.polwithcheck IS NULL)
                    OR (p.polname=@update AND p.polcmd='w' AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)='true')))
            FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass(@table)
                AND p.polname IN (@read,@update)
            """);
        policies.Parameters.AddWithValue("table", table);
        policies.Parameters.AddWithValue("owner", PostgresInstanceApiKeyIssuanceContract.OwnerRole);
        policies.Parameters.AddWithValue("read", PostgresInstanceApiKeyIssuanceContract.OwnerRole + "_read");
        policies.Parameters.AddWithValue("update", PostgresInstanceApiKeyIssuanceContract.OwnerRole + "_update");
        await Assert.That(await policies.ExecuteScalarAsync(token)).IsEqualTo(true);
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) =>
        new(sql, (NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());
}
