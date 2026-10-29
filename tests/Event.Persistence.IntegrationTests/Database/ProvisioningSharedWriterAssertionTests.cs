using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ProvisioningSharedWriterAssertionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExactSourceOwnedWriterPassesReadOnlyAssertionsWithoutChangingCatalogOrData()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration,
            $"INSERT INTO {database.Source(typeof(SystemSetting))} (id) VALUES (1)");
        await Assert.That(await WriterDatabase.ScalarAsync<bool>(database.Migration,
            $"SELECT revision FROM {database.Sql.Name(WriterDatabase.Anchor)} WHERE id = 1")).IsTrue();
        string before = await database.SnapshotAsync();

        foreach (var role in new[] { PrimaryDatabaseRole.Migrator, PrimaryDatabaseRole.Global, PrimaryDatabaseRole.Runtime })
        {
            await using var caller = await database.OpenAsync(role);
            await using var transaction = await caller.Database.BeginTransactionAsync();
            await WriterDatabase.ExecuteAsync(caller, "SET TRANSACTION READ ONLY");
            await database.AssertAsync(caller);
            await database.AssertAsync(caller, allowMigrationAbsence: true);
            await transaction.CommitAsync();
        }

        await Assert.That(await database.SnapshotAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task RestampingAReplacementBodyCannotReplaceSourceOwnedAuthority()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        const string counterfeit = "BEGIN RETURN NEW; END;";
        await WriterDatabase.ExecuteAsync(database.Migration, $"""
            CREATE OR REPLACE FUNCTION {database.Function}() RETURNS trigger
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body${counterfeit}$body$;
            COMMENT ON FUNCTION {database.Function}() IS '{WriterDatabase.Stamp}{WriterDatabase.Hash(counterfeit)}';
            """);

        await database.RejectWithoutRepairAsync("human_provisioning_shared_body_drift");
    }

    [Test]
    public async Task DisabledExpectedTriggerIsRejectedWithoutReenablingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration,
            $"ALTER TABLE {database.Source(typeof(SystemSetting))} DISABLE TRIGGER {WriterDatabase.Trigger}");

        await database.RejectWithoutRepairAsync("human_provisioning_shared_writer_drift");
    }

    [Test]
    public async Task ExpectedFunctionOnTheWrongTriggerEventIsRejectedWithoutRecreatingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration, $"""
            DROP TRIGGER {WriterDatabase.Trigger} ON {database.Source(typeof(SystemSetting))};
            CREATE TRIGGER {WriterDatabase.Trigger} BEFORE UPDATE ON {database.Source(typeof(SystemSetting))}
                FOR EACH ROW EXECUTE FUNCTION {database.Function}();
            ALTER TABLE {database.Source(typeof(SystemSetting))} ENABLE ALWAYS TRIGGER {WriterDatabase.Trigger};
            """);

        await database.RejectWithoutRepairAsync("human_provisioning_shared_writer_drift");
    }

    [Test]
    public async Task ExpectedTriggerBoundToAnotherFunctionIsRejectedWithoutRetargetingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration, $"""
            CREATE FUNCTION {database.Sql.Name("other_writer")}() RETURNS trigger
                LANGUAGE plpgsql AS $body$BEGIN RETURN NEW; END;$body$;
            DROP TRIGGER {WriterDatabase.Trigger} ON {database.Source(typeof(SystemSetting))};
            CREATE TRIGGER {WriterDatabase.Trigger} BEFORE INSERT ON {database.Source(typeof(SystemSetting))}
                FOR EACH ROW EXECUTE FUNCTION {database.Sql.Name("other_writer")}();
            ALTER TABLE {database.Source(typeof(SystemSetting))} ENABLE ALWAYS TRIGGER {WriterDatabase.Trigger};
            """);

        await database.RejectWithoutRepairAsync("human_provisioning_shared_writer_drift");
    }

    [Test]
    public async Task AdditionalTargetUsingTheExpectedFunctionIsRejectedWithoutRemovingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration, $"""
            CREATE TRIGGER additional_writer BEFORE INSERT ON {database.Sql.Name("other_source")}
                FOR EACH ROW EXECUTE FUNCTION {database.Function}();
            ALTER TABLE {database.Sql.Name("other_source")} ENABLE ALWAYS TRIGGER additional_writer;
            """);

        await database.RejectWithoutRepairAsync("human_provisioning_shared_target_drift");
    }

    [Test]
    public async Task GuardedRoleStampDriftIsRejectedWithoutRestampingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration,
            $"COMMENT ON ROLE {WriterDatabase.Quote(database.Owner)} IS 'counterfeit-role-stamp'");

        await database.RejectWithoutRepairAsync("human_provisioning_shared_body_drift");
    }

    [Test]
    public async Task UndeclaredPublicExecutionIsRejectedWithoutRevokingIt()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await WriterDatabase.ExecuteAsync(database.Migration,
            $"GRANT EXECUTE ON FUNCTION {database.Function}() TO PUBLIC");

        await database.RejectWithoutRepairAsync("native_privilege_guard_not_ready");
    }

    [Test]
    public async Task WholeAbsenceIsAllowedOnlyForTheEnrolledMigrator()
    {
        await using var database = await WriterDatabase.CreateAsync(fixture);
        await Assert.That(await WriterDatabase.ScalarAsync<bool>(database.Migration,
            $"SELECT ({database.Writer.Presence(database.Migration)})")).IsFalse();
        string before = await database.SnapshotAsync();

        await using (var transaction = await database.Migration.Database.BeginTransactionAsync())
        {
            await WriterDatabase.ExecuteAsync(database.Migration, "SET TRANSACTION READ ONLY");
            await database.AssertAsync(database.Migration, allowMigrationAbsence: true);
            await transaction.CommitAsync();
        }
        foreach (var role in new[] { PrimaryDatabaseRole.Global, PrimaryDatabaseRole.Runtime })
            await database.RejectWithoutRepairAsync("human_provisioning_shared_installation_missing",
                role, allowMigrationAbsence: true);

        await Assert.That(await database.SnapshotAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task AnyPartialFootprintRejectsEvenTheEnrolledMigrator()
    {
        // Exercise each independent Presence branch: no complete installation is required
        // for an orphan role, anchor, routine or name-only trigger to fail closed.
        foreach (string footprint in new[] { "role", "anchor", "routine", "trigger" })
        {
            await using var database = await WriterDatabase.CreateAsync(fixture);
            string ddl = footprint switch
            {
                "role" => $"CREATE ROLE {WriterDatabase.Quote(database.Owner)} NOLOGIN NOINHERIT",
                "anchor" => $"CREATE TABLE {database.Sql.Name(WriterDatabase.Anchor)} (id integer)",
                "routine" => $"""
                    CREATE FUNCTION {database.Function}() RETURNS trigger LANGUAGE plpgsql
                        AS $body$BEGIN RETURN NEW; END;$body$
                    """,
                "trigger" => $"""
                    CREATE FUNCTION {database.Sql.Name("unrelated_writer")}() RETURNS trigger LANGUAGE plpgsql
                        AS $body$BEGIN RETURN NEW; END;$body$;
                    CREATE TRIGGER {WriterDatabase.Trigger} BEFORE INSERT ON {database.Source(typeof(SystemSetting))}
                        FOR EACH ROW EXECUTE FUNCTION {database.Sql.Name("unrelated_writer")}()
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(footprint))
            };
            await WriterDatabase.ExecuteAsync(database.Migration, ddl);
            await Assert.That(await WriterDatabase.ScalarAsync<bool>(database.Migration,
                $"SELECT ({database.Writer.Presence(database.Migration)})")).IsTrue();

            await database.RejectWithoutRepairAsync("native_privilege_guard_not_ready",
                PrimaryDatabaseRole.Migrator, allowMigrationAbsence: true);
        }
    }

    private sealed class WriterDatabase : IAsyncDisposable
    {
        internal const string Trigger = "assertion_writer";
        internal const string Anchor = "assertion_anchor";
        internal const string Stamp = "assertion-writer:v1:";
        private const string Routine = "assertion_write";
        private static readonly Type[] Sources = [typeof(SystemSetting), typeof(TenantSetting)];
        private readonly PostgreSqlContainerFixture _fixture;
        private readonly string _schema;
        private readonly string _executor;

        private WriterDatabase(PostgreSqlContainerFixture fixture, ExploreDbContext migration, string schema)
        {
            _fixture = fixture;
            _schema = schema;
            Migration = migration;
            Sql = new HumanPrincipalSourceSql(migration);
            Owner = schema + "_owner";
            _executor = schema + "_executor";
            // Authority is declared here, never inferred from current role memberships or ACLs.
            var privileges = new PostgresPrivilegeGuard.Contract(schema, [Owner, _executor],
                [new(Routine, "", Owner, [_executor])],
                [new(Anchor, Owner, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
                [
                    new(_executor, fixture.NativeQueueEnrollment.RuntimeLogin, true, false, false),
                    new(_executor, fixture.NativeQueueEnrollment.GlobalLogin, true, false, false)
                ]);
            string body = $"""
                BEGIN
                    UPDATE {Sql.Name(Anchor)} SET revision = NOT revision WHERE id = 1;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'assertion_anchor_missing' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                """;
            Writer = new(Trigger, Routine, Anchor, Stamp,
                [(typeof(SystemSetting), 7), (typeof(TenantSetting), 7)], privileges, body);
        }

        internal ExploreDbContext Migration { get; }
        internal HumanPrincipalSourceSql Sql { get; }
        internal ProvisioningSharedWriterAssertions.Writer Writer { get; }
        internal string Owner { get; }
        internal string Function => Sql.Name(Routine);
        internal string Source(Type type) => Sql.Table(type);

        internal static async Task<WriterDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            fixture.NativeQueueEnrollment.Validate();
            string schema = "writer_assert_" + Guid.CreateVersion7().ToString("N");
            var migration = CreateContext(fixture, PrimaryDatabaseRole.Migrator, schema);
            var database = new WriterDatabase(fixture, migration, schema);
            try
            {
                await migration.Database.OpenConnectionAsync();
                await ExecuteAsync(migration, $"""
                    CREATE SCHEMA {Quote(schema)};
                    GRANT USAGE ON SCHEMA {Quote(schema)}
                        TO {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}, {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)};
                    CREATE TABLE {database.Source(typeof(SystemSetting))} (id integer PRIMARY KEY);
                    CREATE TABLE {database.Source(typeof(TenantSetting))} (id integer PRIMARY KEY);
                    CREATE TABLE {database.Sql.Name("other_source")} (id integer PRIMARY KEY);
                    """);
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
            string roles = string.Join("\n", Writer.Privileges.GuardedRoles.Select(role => $"""
                CREATE ROLE {Quote(role)} NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
                COMMENT ON ROLE {Quote(role)} IS '{Stamp}role:{role}';
                GRANT USAGE ON SCHEMA {Quote(_schema)} TO {Quote(role)};
                """));
            string memberships = string.Join("\n", Writer.Privileges.Memberships.Select(edge => $"""
                GRANT {Quote(edge.Parent)} TO {Quote(edge.Member)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
                """));
            string triggers = string.Join("\n", Sources.Select(source => $"""
                CREATE TRIGGER {Trigger} BEFORE INSERT ON {Source(source)}
                    FOR EACH ROW EXECUTE FUNCTION {Function}();
                ALTER TABLE {Source(source)} ENABLE ALWAYS TRIGGER {Trigger};
                """));
            await ExecuteAsync(Migration, $"""
                {roles}
                {memberships}
                CREATE TABLE {Sql.Name(Anchor)} (id smallint PRIMARY KEY CHECK (id = 1), revision boolean NOT NULL);
                INSERT INTO {Sql.Name(Anchor)} (id, revision) VALUES (1, false);
                GRANT SELECT (id, revision), UPDATE (revision) ON {Sql.Name(Anchor)} TO {Quote(Owner)};
                GRANT CREATE ON SCHEMA {Quote(_schema)} TO {Quote(Owner)};
                CREATE FUNCTION {Function}() RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                    SET search_path = pg_catalog AS $body${Writer.Body}$body$;
                ALTER FUNCTION {Function}() OWNER TO {Quote(Owner)};
                REVOKE CREATE ON SCHEMA {Quote(_schema)} FROM {Quote(Owner)};
                REVOKE ALL ON FUNCTION {Function}() FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {Function}() TO {Quote(_executor)};
                COMMENT ON FUNCTION {Function}() IS '{Stamp}{Hash(Writer.Body)}';
                {triggers}
                """);
            // Every negative test first proves its unmodified native contract is admissible.
            await AssertAsync(Migration);
        }

        internal Task AssertAsync(ExploreDbContext caller, bool allowMigrationAbsence = false) =>
            ExecuteAsync(caller, $"""
                DO $assertion$ BEGIN
                    {(allowMigrationAbsence
                        ? Writer.AssertInstalledOrMigrationAbsent(Migration, _fixture.NativeQueueEnrollment)
                        : Writer.AssertInstalled(Migration))}
                END $assertion$;
                """);

        internal async Task RejectWithoutRepairAsync(string message,
            PrimaryDatabaseRole role = PrimaryDatabaseRole.Runtime, bool allowMigrationAbsence = false)
        {
            string before = await SnapshotAsync();
            await using var caller = await OpenAsync(role);
            await using var transaction = await caller.Database.BeginTransactionAsync();
            await ExecuteAsync(caller, "SET TRANSACTION READ ONLY");

            var error = await Assert.ThrowsAsync<PostgresException>(() => AssertAsync(caller, allowMigrationAbsence));

            await Assert.That(error!.SqlState).IsEqualTo("55000");
            await Assert.That(error.MessageText).IsEqualTo(message);
            await transaction.RollbackAsync();
            await Assert.That(await SnapshotAsync()).IsEqualTo(before);
        }

        internal async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role)
        {
            var context = CreateContext(_fixture, role, _schema);
            try
            {
                await context.Database.OpenConnectionAsync();
                string expected = role switch
                {
                    PrimaryDatabaseRole.Migrator => _fixture.NativeQueueEnrollment.MigratorLogin,
                    PrimaryDatabaseRole.Global => _fixture.NativeQueueEnrollment.GlobalLogin,
                    PrimaryDatabaseRole.Runtime => _fixture.NativeQueueEnrollment.RuntimeLogin,
                    _ => throw new ArgumentOutOfRangeException(nameof(role))
                };
                await Assert.That(await ScalarAsync<string>(context, "SELECT session_user::text")).IsEqualTo(expected);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task<string> SnapshotAsync()
        {
            string catalog = await ScalarAsync<string>(Migration, $"""
                SELECT pg_catalog.jsonb_build_object(
                    'schema', (SELECT pg_catalog.to_jsonb(n) FROM pg_catalog.pg_namespace n WHERE n.nspname = '{_schema}'),
                    'relations', (SELECT jsonb_agg(pg_catalog.to_jsonb(c) ORDER BY c.oid)
                        FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = '{_schema}'),
                    'columns', (SELECT jsonb_agg(pg_catalog.to_jsonb(a) ORDER BY a.attrelid, a.attnum)
                        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
                        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = '{_schema}'),
                    'functions', (SELECT jsonb_agg(pg_catalog.to_jsonb(f) ||
                            jsonb_build_object('stamp', pg_catalog.obj_description(f.oid, 'pg_proc')) ORDER BY f.oid)
                        FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        WHERE n.nspname = '{_schema}'),
                    'triggers', (SELECT jsonb_agg(pg_catalog.to_jsonb(t) ORDER BY t.oid)
                        FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = '{_schema}'),
                    'roles', (SELECT jsonb_agg(pg_catalog.to_jsonb(r) ||
                            jsonb_build_object('stamp', pg_catalog.shobj_description(r.oid, 'pg_authid')) ORDER BY r.oid)
                        FROM pg_catalog.pg_roles r WHERE r.rolname IN ('{Owner}', '{_executor}')),
                    'memberships', (SELECT jsonb_agg(pg_catalog.to_jsonb(m) ORDER BY m.roleid, m.member)
                        FROM pg_catalog.pg_auth_members m WHERE m.roleid IN
                            (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{Owner}', '{_executor}'))
                        OR m.member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{Owner}', '{_executor}'))),
                    'source_rows', (SELECT jsonb_agg(id ORDER BY id) FROM {Source(typeof(SystemSetting))}),
                    'other_source_rows', (SELECT jsonb_agg(id ORDER BY id) FROM {Source(typeof(TenantSetting))}),
                    'extra_source_rows', (SELECT jsonb_agg(id ORDER BY id) FROM {Sql.Name("other_source")})
                )::text
                """);
            bool anchor = await ScalarAsync<bool>(Migration,
                $"SELECT pg_catalog.to_regclass('{Sql.Name(Anchor)}') IS NOT NULL");
            string rows = anchor
                ? await ScalarAsync<string>(Migration,
                    $"SELECT COALESCE(jsonb_agg(pg_catalog.to_jsonb(a) ORDER BY id), '[]'::jsonb)::text FROM {Sql.Name(Anchor)} a")
                : "absent";
            return catalog + "\n" + rows;
        }

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
            command.CommandText = sql;
            return command;
        }

        private static ExploreDbContext CreateContext(PostgreSqlContainerFixture fixture, PrimaryDatabaseRole role, string schema)
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role, schema));
            return new ExploreDbContext(builder.Options);
        }

        internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        internal static string Hash(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(Migration, $"""
                    DROP SCHEMA IF EXISTS {Quote(_schema)} CASCADE;
                    DROP ROLE IF EXISTS {Quote(_executor)}, {Quote(Owner)};
                    """);
            }
            finally
            {
                await Migration.DisposeAsync();
            }
        }
    }
}
