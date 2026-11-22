using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class MixedSettingsNativeContractTests
{
    [Test]
    [Arguments("renamed-column", true)]
    [Arguments("nullable-column", true)]
    [Arguments("type-modifier", true)]
    [Arguments("historical-dropped-column", false)]
    public async Task SourceColumnShapeIsCheckedFreshlyOnTheOriginalConnection(string change, bool reject)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        int backend = await migration.Database.SqlQueryRaw<int>(
            "SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(Tenant));
        string column = names.Column(typeof(Tenant), nameof(Tenant.FullName));
        const string liveColumns = """
            SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_attribute
            WHERE attrelid=pg_catalog.to_regclass({0}) AND attnum>0 AND NOT attisdropped
            """;
        int originalCount = await migration.Database.SqlQueryRaw<int>(liveColumns, table).SingleAsync();
        string statement = change switch
        {
            "renamed-column" => $"ALTER TABLE {table} RENAME COLUMN {column} TO mixed_guard_unexpected_column",
            "nullable-column" => $"ALTER TABLE {table} ALTER COLUMN {column} DROP NOT NULL",
            "type-modifier" => $"ALTER TABLE {table} ALTER COLUMN {column} TYPE character varying(501)",
            "historical-dropped-column" => $"""
                ALTER TABLE {table} ADD COLUMN mixed_guard_dropped_column integer;
                ALTER TABLE {table} DROP COLUMN mixed_guard_dropped_column
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
            await ExecuteGuardStatementAsync(migration, statement);
            await using (var count = migration.Database.GetDbConnection().CreateCommand())
            {
                count.Transaction = transaction.GetDbTransaction();
                count.CommandTimeout = migration.Database.GetCommandTimeout() ?? count.CommandTimeout;
                count.CommandText = liveColumns.Replace("{0}", "@table", StringComparison.Ordinal);
                var parameter = count.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = table;
                count.Parameters.Add(parameter);
                await Assert.That((int)(await count.ExecuteScalarAsync())!).IsEqualTo(originalCount);
            }
            if (reject)
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
                await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
                await Assert.That(rejected.MessageText).IsEqualTo("mixed_settings_source_catalog_drift");
            }
            else
            {
                await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
            }
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        await Assert.That(await migration.Database.SqlQueryRaw<int>(
            "SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync()).IsEqualTo(backend);
    }

    [Test]
    public async Task OwnerExecutionGrantOptionsPreservePrivateGuardAdmission()
    {
        _ = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        await caller.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        string signature = PostgresMixedSettingsContract.FunctionSql(migration, "assert_catalogue") + "()";
        var names = new HumanPrincipalSourceSql(migration);
        string owner = names.Quote(PostgresMixedSettingsContract.OwnerRole);
        string sourceStatement = $"UPDATE {names.Table(typeof(Tenant))}"
            + $" SET {names.Column(typeof(Tenant), nameof(Tenant.Id))} = {names.Column(typeof(Tenant), nameof(Tenant.Id))} WHERE false";
        await migration.Database.ExecuteSqlRawAsync(sourceStatement);
        string revisionSql = $"SELECT revision AS \"Value\" FROM {names.Name(PostgresMixedSettingsContract.SourceTable)} WHERE id = 1";
        long revision = await migration.Database.SqlQueryRaw<long>(revisionSql).SingleAsync();
        const string grantOptionSql = """
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                CROSS JOIN LATERAL pg_catalog.aclexplode(f.proacl) a
                WHERE f.oid=pg_catalog.to_regprocedure({0}) AND a.grantee=f.proowner
                    AND a.privilege_type='EXECUTE' AND a.is_grantable) AS "Value"
            """;
        try
        {
            await ExecuteGuardStatementAsync(migration,
                $"GRANT EXECUTE ON FUNCTION {signature} TO {owner} WITH GRANT OPTION");
            await Assert.That(await migration.Database.SqlQueryRaw<bool>(grantOptionSql, signature).SingleAsync()).IsTrue();
            await PostgresMixedSettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
            await Assert.That(await migration.Database.SqlQueryRaw<bool>(grantOptionSql, signature).SingleAsync()).IsTrue();
            await Assert.That(await migration.Database.SqlQueryRaw<long>(revisionSql).SingleAsync()).IsEqualTo(revision);
            await ExecuteGuardStatementAsync(caller,
                $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "ready")}(NULL::uuid)");
            await migration.Database.ExecuteSqlRawAsync(sourceStatement);
            await Assert.That(await migration.Database.SqlQueryRaw<long>(revisionSql).SingleAsync()).IsEqualTo(revision + 1);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteGuardStatementAsync(caller, $"SELECT {signature}"));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await ExecuteGuardStatementAsync(migration,
                $"REVOKE GRANT OPTION FOR EXECUTE ON FUNCTION {signature} FROM {owner}");
        }
        await Assert.That(await migration.Database.SqlQueryRaw<bool>(grantOptionSql, signature).SingleAsync()).IsFalse();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ReplacedCatalogueGuardRejectsWarmedRuntimeAndSourceCalls(bool restamp) =>
        AssertCatalogueGuardDriftAsync("body", restamp);

    [Test]
    [Arguments("missing")]
    [Arguments("owner")]
    [Arguments("search-path")]
    [Arguments("invoker")]
    [Arguments("immutable")]
    [Arguments("strict")]
    [Arguments("parallel")]
    [Arguments("leakproof")]
    [Arguments("owner-execute")]
    [Arguments("public")]
    [Arguments("global")]
    [Arguments("runtime")]
    [Arguments("email")]
    public Task ChangedCatalogueGuardMetadataRejectsWarmedRuntimeAndSourceCalls(string attack) =>
        AssertCatalogueGuardDriftAsync(attack, restamp: false);

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task CatalogueGuardCannotBeInvokedByAnEnrolledService(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var caller = Open(role, role == PrimaryDatabaseRole.Runtime ? seeded.Tenant : null);
        await caller.Database.OpenConnectionAsync();
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteGuardStatementAsync(caller,
                $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "assert_catalogue")}()"));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        if (role == PrimaryDatabaseRole.Global)
            await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
    }

    private static async Task ExecuteGuardStatementAsync(ExploreDbContext context, string statement)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertCatalogueGuardDriftAsync(string attack, bool restamp)
    {
        _ = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        await caller.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        string ready = $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "ready")}(NULL::uuid)";
        await caller.Database.ExecuteSqlRawAsync(ready);
        int backend = await caller.Database.SqlQueryRaw<int>(
            "SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string sourceStatement = $"UPDATE {names.Table(typeof(Tenant))}"
            + $" SET {names.Column(typeof(Tenant), nameof(Tenant.Id))} = {names.Column(typeof(Tenant), nameof(Tenant.Id))} WHERE false";
        await migration.Database.ExecuteSqlRawAsync(sourceStatement);
        string revisionSql = $"SELECT revision AS \"Value\" FROM {names.Name(PostgresMixedSettingsContract.SourceTable)} WHERE id = 1";
        long revision = await migration.Database.SqlQueryRaw<long>(revisionSql).SingleAsync();

        string function = PostgresMixedSettingsContract.FunctionSql(migration, "assert_catalogue");
        string signature = function + "()";
        await Assert.That(await migration.Database.SqlQueryRaw<bool>(
            "SELECT pg_catalog.to_regprocedure({0}) IS NOT NULL AS \"Value\"", signature).SingleAsync()).IsTrue();
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        const string fingerprintSql = """
            SELECT COALESCE((SELECT pg_catalog.md5(ROW(f.proowner,f.prosecdef,f.provolatile,f.proisstrict,
                f.proparallel,f.proleakproof,f.proconfig,f.proacl,f.prosrc,
                pg_catalog.obj_description(f.oid,'pg_proc'))::text)
                FROM pg_catalog.pg_proc f WHERE f.oid=pg_catalog.to_regprocedure({0})),'absent') AS "Value"
            """;
        string original = await migration.Database.SqlQueryRaw<string>(fingerprintSql, signature).SingleAsync();
        string? recipient = attack switch
        {
            "public" => "PUBLIC",
            "global" => names.Quote(fixture.NativeQueueEnrollment.GlobalLogin),
            "runtime" => names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin),
            "email" => names.Quote(PostgresEmailDeliveryPolicyContract.OwnerRole),
            _ => null
        };
        string change = attack switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {function}() RETURNS void
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$ BEGIN RETURN; END; $body$
                """,
            "missing" => $"DROP FUNCTION {signature}",
            "owner" => $"ALTER FUNCTION {signature} OWNER TO {names.Quote(fixture.NativeQueueEnrollment.MigratorLogin)}",
            "search-path" => $"ALTER FUNCTION {signature} SET search_path = public",
            "invoker" => $"ALTER FUNCTION {signature} SECURITY INVOKER",
            "immutable" => $"ALTER FUNCTION {signature} IMMUTABLE",
            "strict" => $"ALTER FUNCTION {signature} STRICT",
            "parallel" => $"ALTER FUNCTION {signature} PARALLEL SAFE",
            "leakproof" => $"ALTER FUNCTION {signature} LEAKPROOF",
            "owner-execute" => $"REVOKE EXECUTE ON FUNCTION {signature} FROM {names.Quote(PostgresMixedSettingsContract.OwnerRole)}",
            _ when recipient is not null => $"GRANT EXECUTE ON FUNCTION {signature} TO {recipient}",
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };
        try
        {
            await migration.Database.ExecuteSqlRawAsync(change);
            if (restamp)
                await ExecuteGuardStatementAsync(migration, $"""
                    DO $stamp$ DECLARE fingerprint text; BEGIN
                        SELECT 'mixed-settings:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex')
                            INTO fingerprint FROM pg_catalog.pg_proc WHERE oid=pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}');
                        EXECUTE pg_catalog.format('COMMENT ON FUNCTION {signature} IS %L',fingerprint);
                    END $stamp$
                    """);
            string changed = await migration.Database.SqlQueryRaw<string>(fingerprintSql, signature).SingleAsync();
            await Assert.That(changed).IsNotEqualTo(original);

            var runtimeRejected = await Assert.ThrowsAsync<PostgresException>(() =>
                caller.Database.ExecuteSqlRawAsync(ready));
            await Assert.That(runtimeRejected!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(runtimeRejected.MessageText).IsEqualTo("mixed_settings_catalogue_guard_drift");
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            var sourceRejected = await Assert.ThrowsAsync<PostgresException>(() =>
                migration.Database.ExecuteSqlRawAsync(sourceStatement));
            await Assert.That(sourceRejected!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(await migration.Database.SqlQueryRaw<long>(revisionSql).SingleAsync()).IsEqualTo(revision);
            await Assert.That(await caller.Database.SqlQueryRaw<int>(
                "SELECT pg_catalog.pg_backend_pid() AS \"Value\"").SingleAsync()).IsEqualTo(backend);

            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(await migration.Database.SqlQueryRaw<string>(fingerprintSql, signature).SingleAsync())
                .IsEqualTo(changed);
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await ExecuteGuardStatementAsync(migration,
                $"ALTER FUNCTION {signature} OWNER TO {names.Quote(PostgresMixedSettingsContract.OwnerRole)}");
            await ExecuteGuardStatementAsync(migration, $"REVOKE ALL ON FUNCTION {signature} FROM PUBLIC");
            await ExecuteGuardStatementAsync(migration,
                $"GRANT EXECUTE ON FUNCTION {signature} TO {names.Quote(PostgresMixedSettingsContract.OwnerRole)}");
            if (recipient is not null && recipient != "PUBLIC")
                await ExecuteGuardStatementAsync(migration, $"REVOKE EXECUTE ON FUNCTION {signature} FROM {recipient}");
            await ExecuteGuardStatementAsync(migration,
                $"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await Assert.That(await migration.Database.SqlQueryRaw<string>(fingerprintSql, signature).SingleAsync())
            .IsEqualTo(original);
    }
}
