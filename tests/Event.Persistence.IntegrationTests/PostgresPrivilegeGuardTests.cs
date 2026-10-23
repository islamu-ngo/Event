using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence.Security;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresPrivilegeGuardTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task InheritedColumnReadIsRequiredAndInheritedGrantOptionStillClosesExecution()
    {
        await using var database = await GuardDatabase.CreateAsync(fixture, inheritedRead: true);
        await Assert.That(await database.ReadyAsync()).IsTrue();
        await Assert.That(await database.CallAsync()).IsEqualTo(7);
        await database.ExecuteAsync($"REVOKE SELECT (id) ON {database.Table} FROM {database.Outsider}");
        await database.RequireClosedAsync();
        await database.ExecuteAsync($"GRANT SELECT (id) ON {database.Table} TO {database.Outsider} WITH GRANT OPTION");
        await database.RequireClosedAsync();
    }

    [Test]
    public async Task DeclaredTableReadCoversItsColumnsWithoutAdmittingAnotherPrivilege()
    {
        await using var database = await GuardDatabase.CreateAsync(fixture, tableRead: true);
        await Assert.That(await database.ReadyAsync()).IsTrue();
        await Assert.That(await database.CallAsync()).IsEqualTo(7);
        await database.ExecuteAsync($"GRANT UPDATE (private_value) ON {database.Table} TO {database.Owner}");
        await database.RequireClosedAsync();
    }

    [Test]
    public async Task ExactBaselineAndIntentionalReinstallKeepRestrictedCapabilityUsable()
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        await Assert.That(await database.CallAsync()).IsEqualTo(7);
        await database.InstallAsync();
        await Assert.That(await database.CallAsync()).IsEqualTo(7);
        await Assert.That(await database.ReadyAsync()).IsTrue();
        await RequireSqlStateAsync(() => database.CallAsync($"SELECT private_value FROM {database.Table}"), "42501");
    }

    [Test]
    public async Task UnrelatedRoutineGranteeBlocksDirectCallsAndReinstallWithoutRevokingAdministratorGrant()
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        await database.ExecuteAsync($"GRANT EXECUTE ON FUNCTION {database.Function}() TO {database.Outsider}");
        await database.RequireClosedAsync();
        await RequireSqlStateAsync(database.InstallAsync, "55000");
        await Assert.That(await database.ScalarAsync<bool>(
            $"SELECT pg_catalog.has_function_privilege('{database.Outsider}', '{database.Function}()', 'EXECUTE')"))
            .IsTrue();
        await database.RequireClosedAsync();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("executor")]
    [Arguments("migrator")]
    public async Task ContractOwnedPrivateColumnGrantClosesExecutionAndExplicitReinstallRepairsIt(string role)
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        string grantee = database.Role(role);
        await database.ExecuteAsync($"GRANT UPDATE (private_value) ON {database.Table} TO {grantee}");
        await database.RequireClosedAsync();
        await database.InstallAsync();
        await Assert.That(await database.ReadyAsync()).IsTrue();
        await Assert.That(await database.CallAsync()).IsEqualTo(7);
        await Assert.That(await database.ScalarAsync<bool>(
            $"SELECT pg_catalog.has_column_privilege('{grantee}', '{database.Table}', 'private_value', 'UPDATE')"))
            .IsFalse();
    }

    [Test]
    public async Task PublicPrivateColumnPrivilegeIsEffectiveAndNeverSilentlyRepaired()
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        await database.ExecuteAsync($"GRANT SELECT (private_value) ON {database.Table} TO PUBLIC");
        await database.RequireClosedAsync();
        await RequireSqlStateAsync(database.InstallAsync, "55000");
        await Assert.That(await database.ScalarAsync<bool>(
            $"SELECT pg_catalog.has_column_privilege('{database.Outsider}', '{database.Table}', 'private_value', 'SELECT')"))
            .IsTrue();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("executor")]
    [Arguments("migrator")]
    public async Task UnrelatedParentEvenWithoutInheritOrSetClosesExecutionAndReinstall(string role)
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        await database.ExecuteAsync(
            $"GRANT {database.Outsider} TO {database.Role(role)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE");
        await database.RequireClosedAsync();
        await RequireSqlStateAsync(database.InstallAsync, "55000");
        await Assert.That(await database.ScalarAsync<bool>($"""
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                WHERE parent.rolname = '{database.Outsider}' AND member.rolname = '{database.Role(role)}')
            """)).IsTrue();
    }

    [Test]
    [Arguments("INHERIT TRUE")]
    [Arguments("SET FALSE")]
    [Arguments("ADMIN TRUE")]
    public async Task ChangedExpectedMembershipOptionsCloseExecutionAndAreNotNormalized(string option)
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        await database.ExecuteAsync($"GRANT {database.Owner} TO {database.Migrator} WITH {option}");
        await database.RequireClosedAsync();
        await RequireSqlStateAsync(database.InstallAsync, "55000");
        await database.RequireClosedAsync();
    }

    [Test]
    [Arguments("table")]
    [Arguments("grant_option")]
    [Arguments("routine_grant_option")]
    [Arguments("public_execute")]
    [Arguments("maintain")]
    [Arguments("role_attributes")]
    [Arguments("overload")]
    [Arguments("owner")]
    [Arguments("search_path")]
    [Arguments("missing_column")]
    [Arguments("missing_membership")]
    [Arguments("incoming_membership")]
    [Arguments("other_table")]
    public async Task CatalogOrEffectiveAuthorityDriftClosesDirectExecution(string drift)
    {
        await using var database = await GuardDatabase.CreateAsync(fixture);
        string alteration = drift switch
        {
            "table" => $"GRANT SELECT ON {database.Table} TO {database.Owner}",
            "grant_option" => $"GRANT SELECT (id) ON {database.Table} TO {database.Owner} WITH GRANT OPTION",
            "routine_grant_option" => $"GRANT EXECUTE ON FUNCTION {database.Function}() TO {database.Executor} WITH GRANT OPTION",
            "public_execute" => $"GRANT EXECUTE ON FUNCTION {database.Function}() TO PUBLIC",
            "maintain" => $"GRANT MAINTAIN ON {database.Table} TO {database.Executor}",
            "role_attributes" => $"ALTER ROLE {database.Owner} INHERIT",
            "overload" => $"""
                CREATE FUNCTION {database.Function}(integer) RETURNS integer LANGUAGE sql
                    AS 'SELECT 1';
                """,
            "owner" => $"ALTER FUNCTION {database.Function}() OWNER TO {database.Outsider}",
            "search_path" => $"ALTER FUNCTION {database.Function}() SET search_path = public, pg_catalog",
            "missing_column" => $"ALTER TABLE {database.Table} DROP COLUMN id",
            "missing_membership" => $"REVOKE {database.Owner} FROM {database.Migrator}",
            "incoming_membership" =>
                $"GRANT {database.Owner} TO {database.Outsider} WITH INHERIT FALSE, SET TRUE",
            "other_table" => $"""
                CREATE TABLE {database.Schema}.unlisted (secret integer);
                GRANT SELECT (secret) ON {database.Schema}.unlisted TO {database.Owner};
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await database.ExecuteAsync(alteration);
        await database.RequireClosedAsync();
    }

    private static async Task RequireSqlStateAsync(Func<Task> operation, string state)
    {
        PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(operation);
        await Assert.That(exception!.SqlState).IsEqualTo(state);
    }

    private sealed class GuardDatabase : IAsyncDisposable
    {
        private readonly NpgsqlConnection _admin;
        private readonly string _login;
        private readonly string _connectionString;
        private readonly PostgresPrivilegeGuard.Contract _contract;

        private GuardDatabase(NpgsqlConnection admin, string connectionString, string prefix, string migrationLogin,
            bool tableRead, bool inheritedRead)
        {
            _admin = admin;
            Schema = prefix;
            Owner = prefix + "_owner";
            Migrator = prefix + "_migrator";
            Executor = prefix + "_executor";
            Outsider = prefix + "_outsider";
            _login = prefix + "_login";
            _connectionString = connectionString;
            _readGrantee = inheritedRead ? Outsider : Owner;
            _contract = new(Schema, [Owner, Migrator, Executor],
                [new("take", "", Owner, [Executor])],
                [new("queue", Owner, tableRead ? ["SELECT"] : [], tableRead ? [] : [new("id", ["SELECT"])])],
                [
                    new(Owner, Migrator, Inherit: false, Set: true, Admin: false),
                    new(Migrator, migrationLogin, Inherit: false, Set: true, Admin: false),
                    new(Executor, _login, Inherit: true, Set: false, Admin: false),
                    ..(inheritedRead
                        ? new PostgresPrivilegeGuard.Membership[] { new(Outsider, Owner, true, false, false) }
                        : [])
                ]);
        }

        private readonly string _readGrantee;
        internal string Schema { get; }
        internal string Owner { get; }
        internal string Migrator { get; }
        internal string Executor { get; }
        internal string Outsider { get; }
        internal string Table => Schema + ".queue";
        internal string Function => Schema + ".take";
        internal string Role(string role) => role switch
        {
            "owner" => Owner,
            "migrator" => Migrator,
            "executor" => Executor,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

        internal static async Task<GuardDatabase> CreateAsync(
            PostgreSqlContainerFixture fixture, bool tableRead = false, bool inheritedRead = false)
        {
            string prefix = "guard_" + Guid.CreateVersion7().ToString("N");
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var restricted = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = prefix + "_login",
                Password = password,
                Pooling = false,
                SearchPath = "pg_catalog"
            };
            var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var identity = new NpgsqlCommand("SELECT current_user::text", admin);
            string migrationLogin = (string)(await identity.ExecuteScalarAsync())!;
            var database = new GuardDatabase(admin, restricted.ConnectionString, prefix, migrationLogin, tableRead, inheritedRead);
            await using (var transaction = await admin.BeginTransactionAsync())
            {
                await database.ExecuteAsync($"""
                    CREATE SCHEMA {database.Schema};
                    CREATE ROLE {database.Owner} NOLOGIN NOINHERIT;
                    CREATE ROLE {database.Migrator} NOLOGIN NOINHERIT;
                    CREATE ROLE {database.Executor} NOLOGIN NOINHERIT;
                    CREATE ROLE {database.Outsider} NOLOGIN NOINHERIT;
                    CREATE ROLE {database._login} LOGIN INHERIT PASSWORD '{password}';
                    GRANT {database.Owner} TO {database.Migrator} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    GRANT {database.Migrator} TO "{migrationLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                        WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    GRANT {database.Executor} TO {database._login} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
                    {(inheritedRead ? $"GRANT {database.Outsider} TO {database.Owner} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;" : "")}
                    GRANT USAGE ON SCHEMA {database.Schema} TO {database.Owner}, {database.Executor};
                    CREATE TABLE {database.Table} (id integer, private_value text);
                    INSERT INTO {database.Table} VALUES (7, 'private');
                    """);
                await transaction.CommitAsync();
            }
            try
            {
                await database.InstallAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        internal async Task InstallAsync()
        {
            await using var transaction = await _admin.BeginTransactionAsync();
            await ExecuteAsync($"""
                DO $graph$ BEGIN
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(_contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                END $graph$;
                REVOKE ALL ON TABLE {Table} FROM {Owner}, {Executor}, {Migrator};
                REVOKE ALL (id, private_value) ON {Table} FROM {Owner}, {Executor}, {Migrator};
                GRANT SELECT {(_contract.Relations.Single().Privileges.Count == 0 ? "(id)" : "")} ON {Table} TO {_readGrantee};
                CREATE OR REPLACE FUNCTION {Function}() RETURNS integer
                    LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
                    BEGIN
                        {PostgresPrivilegeGuard.BuildAssertSql(_contract)}
                        RETURN (SELECT id FROM {Table});
                    END $body$;
                ALTER FUNCTION {Function}() OWNER TO {Owner};
                REVOKE ALL ON FUNCTION {Function}() FROM PUBLIC, {Executor};
                GRANT EXECUTE ON FUNCTION {Function}() TO {Executor};
                DO $ready$ BEGIN {PostgresPrivilegeGuard.BuildAssertSql(_contract)} END $ready$;
                """);
            await transaction.CommitAsync();
        }

        internal async Task RequireClosedAsync()
        {
            await Assert.That(await ReadyAsync()).IsFalse();
            await RequireSqlStateAsync(() => CallAsync(), "55000");
        }

        internal Task<bool> ReadyAsync() => ScalarAsync<bool>($"SELECT {PostgresPrivilegeGuard.BuildReadySql(_contract)}");

        internal async Task<int> CallAsync(string? sql = null)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql ?? $"SELECT {Function}()", connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, _admin);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var command = new NpgsqlCommand(sql, _admin);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await ExecuteAsync($"""
                DROP SCHEMA {Schema} CASCADE;
                DROP ROLE {_login}, {Outsider}, {Executor}, {Migrator}, {Owner};
                """);
            await _admin.DisposeAsync();
        }
    }
}
