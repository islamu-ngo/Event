using System.Data;
using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresHumanAuthorizationRouteContractTests(PostgreSqlContainerFixture fixture)
{
    private static readonly string[] RouteKeys =
    [
        GovernanceSettingKeys.Security.AuthorizationProvider,
        GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
        GovernanceSettingKeys.Cerbos.CustomEndpoint,
        GovernanceSettingKeys.Cerbos.GrpcEndpoint,
        GovernanceSettingKeys.Cerbos.Mode,
        GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled
    ];

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, false)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true)]
    [Arguments(PrimaryDatabaseRole.Global, 2, false)]
    [Arguments(PrimaryDatabaseRole.Global, 2, true)]
    [Arguments(PrimaryDatabaseRole.Global, 3, false)]
    [Arguments(PrimaryDatabaseRole.Global, 3, true)]
    public async Task ReinstallationPreservesEnrolledSixKeyProjection(
        PrimaryDatabaseRole role, int purpose, bool acquire)
    {
        await using var database = await RouteDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        string catalog = await database.CatalogAsync();
        await database.InstallAsync();
        await Assert.That(await database.CatalogAsync()).IsEqualTo(catalog);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.ReadAsync(caller, purpose, tenant, acquire);

        await Assert.That(facts.EnumerateObject().Count()).IsEqualTo(2);
        var system = facts.GetProperty("System").EnumerateArray().ToArray();
        await Assert.That(system.Length).IsEqualTo(6);
        foreach (string key in RouteKeys)
        {
            var row = system.Single(row => row.GetProperty("Key").GetString() == key);
            await Assert.That(row.EnumerateObject().Count()).IsEqualTo(7);
            await Assert.That(row.GetProperty("Value").GetString()).IsEqualTo("system:" + key);
            await Assert.That(row.GetProperty("ValueType").GetInt32()).IsEqualTo(1);
            await Assert.That(row.GetProperty("IsLocked").GetBoolean()).IsTrue();
            await Assert.That(row.GetProperty("Description").GetString()).IsEqualTo("route description");
            await Assert.That(row.GetProperty("Category").GetString()).IsEqualTo("route category");
            var allowed = row.GetProperty("AllowedValues");
            await Assert.That(allowed.GetArrayLength()).IsEqualTo(2);
            await Assert.That(allowed[0].GetString()).IsEqualTo("local");
            await Assert.That(allowed[1].GetString()).IsEqualTo("cerbos");
        }
        var selected = facts.GetProperty("Tenant").EnumerateArray().ToArray();
        await Assert.That(selected.Length).IsEqualTo(tenant is null ? 0 : 6);
        foreach (string key in tenant is null ? Array.Empty<string>() : RouteKeys)
        {
            var row = selected.Single(row => row.GetProperty("Key").GetString() == key);
            await Assert.That(row.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(row.GetProperty("Value").GetString()).IsEqualTo("selected:" + key);
            await Assert.That(row.GetProperty("IsLocked").GetBoolean()).IsFalse();
        }
        // Serializable is the acquisition precondition, not a membership exclusion lock.
        // The policy-source-set primitive composes that exclusion separately.
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 3, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 1, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 4, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.ReadCommitted)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.RepeatableRead)]
    public async Task WrongLoginPurposeOrIsolationCannotAcquire(
        PrimaryDatabaseRole role, int purpose, IsolationLevel isolation)
    {
        await using var database = await RouteDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Global ? null : database.Tenant;
        await database.SetTenantAsync(caller, tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(isolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, purpose, tenant, acquire: true));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        if (role != PrimaryDatabaseRole.EmailPolicyInstanceMutation)
            await Assert.That(error.MessageText).IsEqualTo("human_route_caller_mismatch");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("different")]
    [Arguments("missing-session")]
    [Arguments("missing-argument")]
    [Arguments("empty")]
    [Arguments("global-tenant")]
    public async Task TenantSubstitutionOrMissingTenantFailsClosed(string scenario)
    {
        await using var database = await RouteDatabase.CreateAsync(fixture);
        bool global = scenario == "global-tenant";
        await using var caller = await database.OpenAsync(global ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime);
        Guid? argument = scenario == "missing-argument" ? null
            : scenario == "empty" ? Guid.Empty : database.Tenant;
        Guid? session = scenario == "different" ? database.OtherTenant
            : scenario == "missing-session" ? null : argument;
        await database.SetTenantAsync(caller, session);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, global ? 2 : 1, argument));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("human_route_caller_mismatch");
    }

    [Test]
    public async Task OwnerHasOnlyMappedColumnSelectAndCannotMutateSources()
    {
        await using var database = await RouteDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var owner = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await RouteDatabase.ExecuteAsync(owner, $"SET ROLE {PostgresHumanAuthorizationRouteContract.OwnerRole}");
        string before = await database.SourceRowsAsync();
        foreach (var source in HumanAuthorizationRouteSql.Sources)
        {
            string table = database.Sql.Table(source.Type);
            string columns = string.Join(",", source.Columns.Select(property => database.Sql.Column(source.Type, property)));
            await RouteDatabase.ExecuteAsync(owner, $"SELECT {columns} FROM {table}");
            foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER", "MAINTAIN" })
                await Assert.That(await RouteDatabase.ScalarAsync<bool>(database.Migration,
                    $"SELECT has_table_privilege('{PostgresHumanAuthorizationRouteContract.OwnerRole}', {RouteDatabase.Literal(table)}, '{privilege}')"))
                    .IsFalse();
            foreach (string property in source.Columns)
                await Assert.That(await RouteDatabase.ScalarAsync<bool>(database.Migration,
                    $"SELECT has_column_privilege('{PostgresHumanAuthorizationRouteContract.OwnerRole}', {RouteDatabase.Literal(table)}, {RouteDatabase.Literal(database.Sql.ColumnName(source.Type, property))}, 'SELECT')"))
                    .IsTrue();
            foreach (string mutation in new[]
            {
                $"INSERT INTO {table} ({columns}) SELECT {columns} FROM {table}",
                $"UPDATE {table} SET {database.Sql.Column(source.Type, "Value")} = 'changed'",
                $"DELETE FROM {table}"
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => RouteDatabase.ExecuteAsync(owner, mutation));
                await Assert.That(error!.SqlState).IsEqualTo("42501");
            }
        }
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        var directRead = await Assert.ThrowsAsync<PostgresException>(() =>
            RouteDatabase.ExecuteAsync(runtime, $"SELECT {database.Sql.Column(typeof(SystemSetting), "Value")} FROM {database.Sql.Table(typeof(SystemSetting))}"));
        await Assert.That(directRead!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("column", "human_route_source_drift")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("role", "human_route_role_drift")]
    [Arguments("routine", "human_route_routine_drift")]
    [Arguments("body", "human_route_exact_body_drift")]
    [Arguments("self-restamp", "human_route_exact_body_drift")]
    [Arguments("rls", "human_route_tenant_policy_drift")]
    [Arguments("tenant-policy", "human_route_tenant_policy_drift")]
    [Arguments("extra-policy", "human_route_rls_drift")]
    public async Task AdmissionAndReinstallationRefuseDriftWithoutRepair(string drift, string message)
    {
        await using var database = await RouteDatabase.CreateAsync(fixture);
        string observe = PostgresHumanAuthorizationRouteContract.ObserveFunctionSql(database.Migration);
        string tenantTable = database.Sql.Table(typeof(TenantSetting));
        const string signature = "(integer,uuid)";
        const string counterfeit = "BEGIN RETURN '{}'::jsonb; END;";
        string ddl = drift switch
        {
            "column" => $"ALTER TABLE {database.Sql.Table(typeof(SystemSetting))} ALTER COLUMN {database.Sql.Column(typeof(SystemSetting), "IsLocked")} DROP NOT NULL",
            "acl" => $"GRANT EXECUTE ON FUNCTION {observe}{signature} TO PUBLIC",
            "role" => $"COMMENT ON ROLE {PostgresHumanAuthorizationRouteContract.OwnerRole} IS 'counterfeit'",
            "routine" => $"ALTER FUNCTION {observe}{signature} STABLE",
            "body" or "self-restamp" => $"""
                CREATE OR REPLACE FUNCTION {observe}(p_purpose integer,p_tenant uuid) RETURNS jsonb
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                """ + (drift == "self-restamp" ? $"""
                DO $stamp$ BEGIN
                    EXECUTE pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                        {RouteDatabase.Literal(observe + signature)},
                        'human-authorization-route:v1:' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to({RouteDatabase.Literal(counterfeit)},'UTF8')),'hex'));
                END $stamp$;
                """ : ""),
            "rls" => $"ALTER TABLE {tenantTable} NO FORCE ROW LEVEL SECURITY",
            "tenant-policy" => $"ALTER POLICY {PostgresTenantRowLevelSecurityModel.PolicyName} ON {tenantTable} USING (true) WITH CHECK (true)",
            "extra-policy" => $"CREATE POLICY counterfeit ON {tenantTable} FOR SELECT TO {PostgresHumanAuthorizationRouteContract.OwnerRole} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await RouteDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);

        var admission = await Assert.ThrowsAsync<PostgresException>(() =>
            RouteDatabase.ExecuteAsync(runtime,
                PostgresHumanAuthorizationRouteContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
        await Assert.That(admission!.SqlState).IsEqualTo("55000");
        await Assert.That(admission.MessageText).IsEqualTo(message);
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(reinstall.MessageText).IsEqualTo(message);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    private sealed class RouteDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();

        internal static async Task<RouteDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Role attestation is cluster-wide. Isolate DDL without changing the original enrollment.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("route_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!)
                .WithPassword(original.Password!)
                .Build();
            try
            {
                await container.StartAsync();
                var migration = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                var database = new RouteDatabase(fixture, container, migration);
                try
                {
                    await migration.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    await database.InstallAsync();
                    return database;
                }
                catch
                {
                    await migration.DisposeAsync();
                    throw;
                }
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }

        private async Task CreateSourcesAsync()
        {
            foreach (var role in new[]
            {
                PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global, PrimaryDatabaseRole.EmailPolicyInstanceMutation
            })
            {
                var original = fixture.GetNativeQueueOptions(role);
                await ExecuteAsync(Migration, $"""
                    CREATE ROLE {Sql.Quote(original.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(original.Password!)};
                    """);
            }
            await ExecuteAsync(Migration, $"CREATE SCHEMA {Sql.Quote(Sql.Schema)}");
            // Only mapped route columns are required; no unrelated fixture graph or foreign keys.
            foreach (var source in HumanAuthorizationRouteSql.Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", source.Columns.Select(name =>
                {
                    var property = entity.FindProperty(name)!;
                    return $"{Sql.Column(source.Type, name)} {property.GetRelationalTypeMapping().StoreType}"
                        + (property.IsNullable ? "" : " NOT NULL");
                }));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(source.Type)} ({columns})");
            }
            var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Single(table => table.TableName == Migration.Model.FindEntityType(typeof(TenantSetting))!.GetTableName());
            await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(tenant, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)} TO
                    {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)},
                    {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)},
                    {Sql.Quote(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation).Username!)};
                """);
        }

        internal Task InstallAsync() =>
            PostgresHumanAuthorizationRouteContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

        internal async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role)
        {
            var context = CreateContext(fixture, container, role);
            try
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await ScalarAsync<string>(context, "SELECT session_user::text"))
                    .IsEqualTo(fixture.GetNativeQueueOptions(role).Username!);
                await SetTenantAsync(context, null);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task SetTenantAsync(ExploreDbContext context, Guid? tenant)
        {
            await using var command = Command(context, "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, false)");
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString("D") ?? ""));
            await command.ExecuteScalarAsync();
        }

        internal async Task<JsonElement> ReadAsync(ExploreDbContext context, int purpose, Guid? tenant, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanAuthorizationRouteContract.AcquireFunctionSql(Migration)
                : PostgresHumanAuthorizationRouteContract.ObserveFunctionSql(Migration);
            await using var command = Command(context, $"SELECT {routine}(@purpose,@tenant)");
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = purpose });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return document.RootElement.Clone();
        }

        internal async Task SeedAsync()
        {
            foreach (string key in RouteKeys.Append("unrelated.route.setting"))
            {
                await InsertAsync(typeof(SystemSetting), ("SettingKey", key), ("Value", "system:" + key),
                    ("SettingValueTypeId", 1), ("IsLocked", true), ("Description", "route description"),
                    ("Category", "route category"), ("AllowedValues", "[\"local\",\"cerbos\"]"));
                await InsertAsync(typeof(TenantSetting), ("SettingKey", key), ("Value", "selected:" + key),
                    ("IsLocked", false), ("TenantId", Tenant));
                await InsertAsync(typeof(TenantSetting), ("SettingKey", key), ("Value", "other:" + key),
                    ("IsLocked", true), ("TenantId", OtherTenant));
            }
        }

        private async Task InsertAsync(Type type, params (string Name, object Value)[] values)
        {
            await using var command = Command(Migration, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", values.Select(value => Sql.Column(type, value.Name)))})
                VALUES ({string.Join(",", values.Select((_, index) => "@p" + index))})
                """);
            for (int index = 0; index < values.Length; index++)
            {
                var parameter = new NpgsqlParameter("p" + index, values[index].Value);
                if (Migration.Model.FindEntityType(type)!.FindProperty(values[index].Name)!
                    .GetRelationalTypeMapping().StoreType == "jsonb")
                    parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> SourceRowsAsync() => ScalarAsync<string>(Migration,
            "SELECT jsonb_build_array(" + string.Join(",", HumanAuthorizationRouteSql.Sources.Select(source =>
                $"(SELECT jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text) FROM {Sql.Table(source.Type)} t)")) + ")::text");

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'attributes', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'routines', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'policies', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'roles', (SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('stamp',shobj_description(r.oid,'pg_authid'))
                    ORDER BY r.oid) FROM pg_roles r),
                'memberships', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m)
            )::text
            """);

        internal static async Task ExecuteAsync(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            await command.ExecuteNonQueryAsync();
        }

        internal static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            return command;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PostgreSqlContainer container, PrimaryDatabaseRole role)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            var original = fixture.GetNativeQueueOptions(role) with
            {
                Host = endpoint.Host,
                Port = endpoint.Port,
                Database = endpoint.Database
            };
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, original);
            return new ExploreDbContext(builder.Options);
        }

        internal static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            try { await Migration.DisposeAsync(); }
            finally { await container.DisposeAsync(); }
        }
    }
}
