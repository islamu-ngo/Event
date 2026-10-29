using System.Data;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class HumanAdministratorNativeCommitFenceTests
{
    [Test]
    [Arguments("public-execute")]
    [Arguments("column")]
    [Arguments("role-set")]
    [Arguments("schema")]
    [Arguments("volatility")]
    [Arguments("body")]
    [Arguments("table-owner")]
    [Arguments("policy")]
    [Arguments("overload")]
    public async Task DriftRejectsEntryAndReinstallationWithoutRepair(string drift)
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresHumanAdministratorCommitAuthorityContract.InstanceFunctionSql(migration);
        string signature = function + "(uuid,integer,text,integer,uuid)";
        string users = Table(migration, typeof(Explore.Domain.User));
        string schema = users[..users.LastIndexOf('.')];
        string login = "\"" + fixture.NativeQueueEnrollment.EmailInstanceMutationLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string owner = PostgresHumanAdministratorCommitAuthorityContract.OwnerRole;
        string originalDefinition = await migration.Database.SqlQuery<string>(
            $"SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({signature})) AS \"Value\"").SingleAsync();
        string migrator = "\"" + fixture.NativeQueueEnrollment.MigratorLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        (string Inject, string Restore) statements = drift switch
        {
            "public-execute" => ($"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC"),
            "column" => ($"GRANT SELECT (id) ON {users} TO {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole}",
                $"REVOKE SELECT (id) ON {users} FROM {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole}"),
            "role-set" => ($"GRANT {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole} TO {login} WITH SET TRUE",
                $"GRANT {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole} TO {login} WITH SET FALSE"),
            "schema" => ($"GRANT CREATE ON SCHEMA {schema} TO {owner}", $"REVOKE CREATE ON SCHEMA {schema} FROM {owner}"),
            "volatility" => ($"ALTER FUNCTION {signature} IMMUTABLE", $"ALTER FUNCTION {signature} VOLATILE"),
            "body" => ($"""
                CREATE OR REPLACE FUNCTION {function}(p_actor uuid, p_provider integer, p_account text, p_purpose integer, p_tenant uuid)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS 'BEGIN RETURN true; END;'
                """, originalDefinition),
            "table-owner" => ($"ALTER TABLE {users} OWNER TO {owner}",
                $"ALTER TABLE {users} OWNER TO {migrator}; GRANT SELECT (id, is_deleted), UPDATE (id) ON {users} TO {owner}"),
            "policy" => ($"ALTER POLICY human_administrator_fence_read ON {users} USING (false)",
                $"ALTER POLICY human_administrator_fence_read ON {users} USING (true)"),
            "overload" => ($"CREATE FUNCTION {function}() RETURNS boolean LANGUAGE sql AS 'SELECT true'",
                $"DROP FUNCTION {function}()"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(statements.Inject);
        try
        {
            await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
            var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailInstance);
            var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(authority, rows, false));
            await transaction.RollbackAsync();
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
            // A second entry still fails: installation must not normalize the contaminated contract.
            await using var retry = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(authority, rows, false));
            await retry.RollbackAsync();
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(statements.Restore);
        }
        await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task NativeCallRejectsChangedSessionTenant()
    {
        var rows = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, rows.Tenant);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailTenant);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_catalog.set_config('app.current_tenant_id', {Guid.CreateVersion7().ToString()}, true)");
        var denied = await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(authority, rows, true));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }
}
