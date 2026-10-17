using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Recovery;
using Explore.Domain;
using Explore.Infrastructure.Recovery;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class TicketingRecoveryHealthNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RegisteredReadinessIncludesForeignFailedCheckpointAndDueWork(bool boundRequest, bool unknown)
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
        await using (var transaction = await setup.Database.BeginTransactionAsync())
        {
            await setup.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            // Unknown cases deliberately retain the real status/state CHECKs.
            // Generated schema must admit the domain's Unknown state with cleared
            // lease and terminal markers; this suite never weakens those checks.
            await TicketingRecoveryHealthTestData.SeedAsync(setup, tenantA, tenantB, unknown);
            await transaction.CommitAsync();
        }
        // Commit is the visibility event. No observer starts before it completes.
        var factory = GlobalFactory();
        await using var request = RuntimeContext(boundRequest ? tenantA : null);
        string checkpoints = TicketingRecoveryHealthTestData.Table<TicketingRecoveryCheckpoint>(setup);
        string runtime = Identifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string schema = setup.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            setup.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetSchema()!);
        await setup.Database.ExecuteSqlRawAsync(
            $"GRANT USAGE ON SCHEMA {schema} TO {runtime}; GRANT SELECT ON {checkpoints} TO {runtime}");
        try
        {
            await Assert.That(await request.TicketingRecoveryCheckpoints.IgnoreQueryFilters()
                .CountAsync(row => row.TenantId == tenantB)).IsEqualTo(0);
            await using var services = TicketingRecoveryHealthTestData.Services(factory, request);
            var report = await services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            var entry = report.Entries[TicketingRecoveryHealthCheck.Name];
            await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(entry.Data.Keys).IsEquivalentTo(TicketingRecoveryHealthCheck.DataKeys);
            await Assert.That(entry.Data["pending_reissues"]).IsEqualTo(2);
            await Assert.That(entry.Data["ambiguous_effects"]).IsEqualTo(unknown ? 1 : 0);
            await Assert.That(entry.Data["dead_lettered_effects"]).IsEqualTo(2);
            await Assert.That(entry.Data["poison_effects"]).IsEqualTo(1);
            await Assert.That(entry.Data["oldest_due_age_seconds"]).IsEqualTo(unknown ? 90d : 30d);
            var health = await services.GetRequiredService<ITicketingRecoveryHealthObserver>()
                .GetAggregateHealthAsync(CancellationToken.None);
            await Assert.That(health).IsEqualTo(new TicketingRecoveryAggregateHealth(
                1, 1, 2, unknown ? 1 : 0, 2, 1,
                TicketingRecoveryHealthTestData.ObservedAt.AddSeconds(unknown ? -90 : -30)));

            await using var global = await factory.CreateDbContextAsync();
            foreach (string table in new[]
            {
                checkpoints, TicketingRecoveryHealthTestData.Table<TicketingRecoveryReissueIntent>(setup),
                TicketingRecoveryHealthTestData.Table<FairReturnOrchestrationEffect>(setup)
            })
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));
                await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                var mutationDenied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await global.Database.ExecuteSqlRawAsync($"DELETE FROM {table} WHERE false"));
                await Assert.That(mutationDenied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            await global.Database.OpenConnectionAsync();
            await using var command = global.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM {PostgresTicketingRecoveryHealthContract.FunctionSql(global)}()";
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.FieldCount).IsEqualTo(7);
            for (int i = 0; i < 6; i++)
                await Assert.That(reader.GetFieldType(i)).IsEqualTo(typeof(int));
            await Assert.That(reader.GetFieldType(6)).IsEqualTo(typeof(DateTime));
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync($"REVOKE SELECT ON {checkpoints} FROM {runtime}");
        }
    }

    [Test]
    public async Task EmptyFleetIsHealthyButWrongLoginAndBoundGlobalCannotObserve()
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await using var services = TicketingRecoveryHealthTestData.Services(GlobalFactory());
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(report.Entries[TicketingRecoveryHealthCheck.Name].Data["pending_reissues"]).IsEqualTo(0);
        await using var runtime = RuntimeContext(null);
        foreach (var caller in new[] { runtime, setup })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await caller.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresTicketingRecoveryHealthContract.FunctionSql(setup)}()"));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        command.Parameters.AddWithValue("tenant", Guid.CreateVersion7().ToString());
        await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = $"SELECT * FROM {PostgresTicketingRecoveryHealthContract.FunctionSql(setup)}()";
        var bound = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        await Assert.That(bound?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("private-column")]
    [Arguments("missing-column")]
    [Arguments("public-column")]
    [Arguments("global-read")]
    [Arguments("wrong-executor")]
    [Arguments("missing-executor")]
    [Arguments("overload")]
    [Arguments("owner-login")]
    [Arguments("unsafe-global")]
    [Arguments("membership-set")]
    [Arguments("membership-admin")]
    [Arguments("membership-inherit")]
    [Arguments("policy-hidden")]
    [Arguments("restrictive-policy")]
    [Arguments("force-disabled")]
    public async Task DriftFailsObservationAndReinstallWithoutNormalizingAuthority(string drift)
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        string function = PostgresTicketingRecoveryHealthContract.FunctionSql(setup);
        string signature = function + "()";
        string table = TicketingRecoveryHealthTestData.Table<TicketingRecoveryCheckpoint>(setup);
        string global = Identifier(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtime = Identifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        const string owner = PostgresTicketingRecoveryHealthContract.OwnerRole;
        const string executor = PostgresTicketingRecoveryHealthContract.ExecutorRole;
        const string migrator = PostgresTicketingRecoveryHealthContract.MigratorRole;
        const string policy = PostgresTicketingRecoveryHealthContract.PolicyName;
        (string inject, string restore, string persists) = drift switch
        {
            "private-column" => ($"GRANT SELECT (manifest_digest) ON {table} TO {owner}",
                $"REVOKE SELECT (manifest_digest) ON {table} FROM {owner}",
                $"SELECT has_column_privilege('{owner}', {Literal(table)}, 'manifest_digest', 'SELECT') AS \"Value\""),
            "missing-column" => ($"REVOKE SELECT (status) ON {table} FROM {owner}",
                $"GRANT SELECT (status) ON {table} TO {owner}",
                $"SELECT NOT has_column_privilege('{owner}', {Literal(table)}, 'status', 'SELECT') AS \"Value\""),
            "public-column" => ($"GRANT SELECT (id) ON {table} TO PUBLIC",
                $"REVOKE SELECT (id) ON {table} FROM PUBLIC",
                $"SELECT has_column_privilege('{executor}', {Literal(table)}, 'id', 'SELECT') AS \"Value\""),
            "global-read" => ($"GRANT SELECT ON {table} TO {global}",
                $"REVOKE SELECT ON {table} FROM {global}",
                $"SELECT has_table_privilege({Literal(fixture.NativeQueueEnrollment.GlobalLogin)}, {Literal(table)}, 'SELECT') AS \"Value\""),
            "wrong-executor" => ($"GRANT EXECUTE ON FUNCTION {signature} TO {runtime}",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM {runtime}",
                $"SELECT has_function_privilege({Literal(fixture.NativeQueueEnrollment.RuntimeLogin)}, {Literal(signature)}, 'EXECUTE') AS \"Value\""),
            "missing-executor" => ($"REVOKE EXECUTE ON FUNCTION {signature} FROM {executor}",
                $"GRANT EXECUTE ON FUNCTION {signature} TO {executor}",
                $"SELECT NOT has_function_privilege('{executor}', {Literal(signature)}, 'EXECUTE') AS \"Value\""),
            "overload" => ($"CREATE FUNCTION {function}(integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}(integer)",
                $"SELECT to_regprocedure({Literal(function + "(integer)")}) IS NOT NULL AS \"Value\""),
            "owner-login" => ($"ALTER ROLE {owner} LOGIN", $"ALTER ROLE {owner} NOLOGIN",
                $"SELECT rolcanlogin AS \"Value\" FROM pg_roles WHERE rolname = '{owner}'"),
            "unsafe-global" => ($"ALTER ROLE {global} BYPASSRLS", $"ALTER ROLE {global} NOBYPASSRLS",
                $"SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}"),
            "membership-set" => ($"GRANT {executor} TO {global} WITH SET TRUE",
                $"GRANT {executor} TO {global} WITH SET FALSE",
                $"SELECT set_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{executor}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "membership-admin" => ($"GRANT {executor} TO {global} WITH ADMIN TRUE",
                $"GRANT {executor} TO {global} WITH ADMIN FALSE",
                $"SELECT admin_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{executor}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "membership-inherit" => ($"GRANT {owner} TO {migrator} WITH INHERIT TRUE",
                $"GRANT {owner} TO {migrator} WITH INHERIT FALSE",
                $"SELECT inherit_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{owner}'::regrole AND member = '{migrator}'::regrole"),
            "policy-hidden" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)",
                $"SELECT pg_get_expr(polqual, polrelid) = 'false' AS \"Value\" FROM pg_policy WHERE polrelid = {Literal(table)}::regclass AND polname = '{policy}'"),
            "restrictive-policy" => ($"CREATE POLICY recovery_health_deny ON {table} AS RESTRICTIVE FOR SELECT TO {owner} USING (false)",
                $"DROP POLICY recovery_health_deny ON {table}",
                $"SELECT EXISTS (SELECT 1 FROM pg_policy WHERE polrelid = {Literal(table)}::regclass AND polname = 'recovery_health_deny') AS \"Value\""),
            "force-disabled" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY",
                $"SELECT NOT relforcerowsecurity AS \"Value\" FROM pg_class WHERE oid = {Literal(table)}::regclass"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        long oid = await setup.Database.SqlQuery<long>(
            $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync();
        await setup.Database.ExecuteSqlRawAsync(inject);
        try
        {
            // A raw enrolled connection reaches the function's own guard, not
            // merely the generic connection interceptor's admission check.
            await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {signature}";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            // The exact matrix guard runs before the ordinary caller check;
            // unsafe Global enrollment is therefore 55000, not caller 42501.
            await Assert.That(denied?.SqlState).IsEqualTo(
                drift == "missing-executor" ? PostgresErrorCodes.InsufficientPrivilege : "55000");
            await using var services = TicketingRecoveryHealthTestData.Services(GlobalFactory());
            var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(report.Entries[TicketingRecoveryHealthCheck.Name].Data.Count).IsEqualTo(0);
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall?.SqlState).IsEqualTo("55000");
            await Assert.That(await setup.Database.SqlQueryRaw<bool>(persists).SingleAsync()).IsTrue();
            await Assert.That(await setup.Database.SqlQuery<long>(
                $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync()).IsEqualTo(oid);
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(restore);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PartialEnrollmentAndOrphanArtifactsCannotBeAdopted(bool allAbsent)
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresTicketingRecoveryHealthContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await using var transaction = await setup.Database.BeginTransactionAsync();
        string[] roles = allAbsent
            ? [PostgresTicketingRecoveryHealthContract.OwnerRole,
                PostgresTicketingRecoveryHealthContract.MigratorRole, PostgresTicketingRecoveryHealthContract.ExecutorRole]
            : [PostgresTicketingRecoveryHealthContract.OwnerRole];
        foreach (string role in roles)
            await setup.Database.ExecuteSqlRawAsync($"ALTER ROLE {role} RENAME TO \"recovery_health_drift_{Guid.CreateVersion7():N}\"");
        await transaction.CreateSavepointAsync("drift");
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await setup.Database.ExecuteSqlRawAsync(
                PostgresTicketingRecoveryHealthContract.BuildSql(setup, fixture.NativeQueueEnrollment)));
        await Assert.That(denied?.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("drift");
        foreach (string role in roles)
            await Assert.That(await setup.Database.SqlQuery<bool>(
                $"SELECT to_regrole({role}) IS NULL AS \"Value\"").SingleAsync()).IsTrue();
        await transaction.RollbackAsync();
    }

    private GlobalExploreDbContextFactory GlobalFactory() =>
        new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);

    private ExploreDbContext RuntimeContext(Guid? tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        return new ExploreDbContext(builder.Options)
        {
            TenantContext = tenant is { } id ? new TicketingRecoveryHealthTestData.TenantIdentity(id) : null
        };
    }

    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
