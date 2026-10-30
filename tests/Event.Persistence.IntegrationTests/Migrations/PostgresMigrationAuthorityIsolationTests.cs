#nullable enable

using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[Property("Category", "ExploreDatabaseMigrator")]
public sealed class PostgresMigrationAuthorityIsolationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task MigrateAndSeedUsesConfiguredNamespaceForPoliciesAndSeedWithoutRuntimeCredentialPromotion()
    {
        string suffix = Guid.CreateVersion7().ToString("N");
        string databaseName = $"migration_namespace_{suffix}";
        string schema = $"event_{suffix}";
        var administrator = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var administration = new NpgsqlConnection(administrator.ConnectionString);
        await administration.OpenAsync();
        await ExecuteAsync(administration, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var migrationOptions = Options(administrator, databaseName,
                administrator.Username!, administrator.Password!, PrimaryDatabaseRole.Migrator) with
            {
                Schema = schema
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PrivacyErasure:Authority:Topology"] = "CoLocated"
                }).Build();
            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                ProductionHostEnvironment.Instance, configuration, migrationOptions, new MigrationOperationLogger());
            await using var verification = new NpgsqlConnection(
                PrimaryDatabaseConfiguration.BuildConnectionString(migrationOptions).ConnectionString);
            await verification.OpenAsync();
            await using var query = verification.CreateCommand();
            query.CommandText = $"""
                SELECT EXISTS (
                    SELECT 1 FROM "{schema}".ui_theme_presets
                    WHERE theme_key = 'enterprise-blue' AND tenant_id IS NULL),
                    NOT EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'islamu_event' AND c.relkind = 'r'),
                    EXISTS (
                        SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = @schema AND c.relname = 'tenant_setting_overrides'
                          AND c.relrowsecurity AND c.relforcerowsecurity
                          AND obj_description(p.oid, 'pg_policy') LIKE 'event-tenant-policy:v1:%');
                """;
            query.Parameters.AddWithValue("schema", schema);
            await using var reader = await query.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsTrue();
            await Assert.That(reader.GetBoolean(1)).IsTrue();
            await Assert.That(reader.GetBoolean(2)).IsTrue();
        }
        finally
        {
            await ExecuteAsync(administration, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
        }
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task MigrateAndSeedAsync_OrdinaryRuntimeCannotOwnMigrationWork_MigratorCompletesAdjustmentsAndSeed()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string databaseName = $"migration_authority_{suffix}";
        string migratorName = $"migration_owner_{suffix}";
        string runtimeName = $"migration_runtime_{suffix}";
        string migratorPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var administrator = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        await using var administration = new NpgsqlConnection(administrator.ConnectionString);
        await administration.OpenAsync();

        try
        {
            // Discovery maintenance provisions roles and FORCE RLS protects seed tables.
            // Only the disposable migration login receives that bootstrap authority.
            await ExecuteAsync(administration,
                $"""
                CREATE ROLE "{migratorName}" LOGIN SUPERUSER PASSWORD '{migratorPassword}';
                CREATE ROLE "{runtimeName}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                    NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
                """);
            await ExecuteAsync(administration, $"CREATE DATABASE \"{databaseName}\" OWNER \"{migratorName}\"");

            PrimaryDatabaseConnectionOptions migrationOptions = Options(
                administrator, databaseName, migratorName, migratorPassword, PrimaryDatabaseRole.Migrator);
            PrimaryDatabaseConnectionOptions runtimeOptions = Options(
                administrator, databaseName, runtimeName, runtimePassword, PrimaryDatabaseRole.Runtime);
            await using ExploreDbContext migration = CreateContext(migrationOptions);
            await migration.Database.OpenConnectionAsync();
            var migrationConnection = (NpgsqlConnection)migration.Database.GetDbConnection();
            await AssertAuthorityAsync(migrationConnection, migratorName, privileged: true);

            // Apply the real application migration chain, not EnsureCreated or synthetic history.
            await ExploreDatabaseMigrator.MigrateAsync(migration, new ConfigurationManager());
            await AssertMigrationHistoryAsync(migration);
            await PostgresModelConstraintApplier.ApplyAsync(migration);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(migration);

            await ExecuteAsync(migrationConnection,
                $"""
                REVOKE ALL ON DATABASE "{databaseName}" FROM PUBLIC;
                GRANT CONNECT ON DATABASE "{databaseName}" TO "{runtimeName}";
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                REVOKE ALL ON SCHEMA islamu_event FROM PUBLIC;
                GRANT USAGE ON SCHEMA islamu_event TO "{runtimeName}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA islamu_event TO "{runtimeName}";
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA islamu_event TO "{runtimeName}";
                REVOKE INSERT, UPDATE, DELETE ON TABLE islamu_event.ui_theme_presets FROM "{runtimeName}";
                DELETE FROM islamu_event.ui_theme_presets;
                ALTER TABLE islamu_event.event_sessions
                    DROP CONSTRAINT ex_event_session_room_no_overlap;
                ALTER TABLE islamu_event.event_sessions DISABLE ROW LEVEL SECURITY;
                ALTER TABLE islamu_event.event_sessions NO FORCE ROW LEVEL SECURITY;
                """);
            await AssertAdjustmentStateAsync(migrationConnection, completed: false);
            await Assert.That(await migration.UiThemePresets.AnyAsync()).IsFalse();

            await using ExploreDbContext runtime = CreateContext(runtimeOptions);
            await runtime.Database.OpenConnectionAsync();
            var runtimeConnection = (NpgsqlConnection)runtime.Database.GetDbConnection();
            await AssertAuthorityAsync(runtimeConnection, runtimeName, privileged: false);
            await AssertRuntimePrivilegesAsync(runtimeConnection);

            // Negative probes are independent of the successful operation required below.
            PostgresException? deniedDdl = await Assert.That(async () =>
                    await ExecuteAsync(runtimeConnection,
                        "ALTER TABLE islamu_event.event_sessions ENABLE ROW LEVEL SECURITY"))
                .Throws<PostgresException>();
            await Assert.That(deniedDdl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            PostgresException? deniedElevation = await Assert.That(async () =>
                    await ExecuteAsync(runtimeConnection, $"SET ROLE \"{migratorName}\""))
                .Throws<PostgresException>();
            await Assert.That(deniedElevation!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await AssertAuthorityAsync(runtimeConnection, runtimeName, privileged: false);
            await AssertAuthorityAsync(migrationConnection, migratorName, privileged: true);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PrivacyErasure:Authority:Topology"] = "CoLocated",
                })
                .Build();
            var logger = new MigrationOperationLogger();

            // This call must complete; a privilege exception is the regression, not success.
            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                ProductionHostEnvironment.Instance,
                configuration,
                migrationOptions,
                logger);

            await Assert.That(logger.Operations).IsEquivalentTo(
                new[] { "Application", "ProviderAdjustments", "DataProtection", "AuthorityCoLocatedPostgreSql", "Seed" },
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await AssertAdjustmentStateAsync(migrationConnection, completed: true);
            await AssertMigrationHistoryAsync(migration);
            await Assert.That(await migration.UiThemePresets.AsNoTracking()
                .AnyAsync(preset => preset.IsSystem && preset.TenantId == null
                    && preset.ThemeKey == "enterprise-blue")).IsTrue();
            await Assert.That(await migration.LocationKinds.AsNoTracking().AnyAsync()).IsTrue();

            var dataProtectionOptions = TestDbContextOptions.Create<DataProtectionKeyContext>();
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(dataProtectionOptions, migrationOptions);
            await using var dataProtection = new DataProtectionKeyContext(dataProtectionOptions.Options);
            await AssertMigrationHistoryAsync(dataProtection);
            var authorityOptions = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(authorityOptions, migrationOptions);
            await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(authorityOptions.Options);
            await AssertMigrationHistoryAsync(authority);

            await AssertAuthorityAsync(runtimeConnection, runtimeName, privileged: false);
            await AssertRuntimePrivilegesAsync(runtimeConnection);
        }
        finally
        {
            // Names belong only to this invocation; no shared database or pool is reset.
            await ExecuteAsync(administration, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
            await ExecuteAsync(administration,
                $"""
                DROP ROLE IF EXISTS "{runtimeName}";
                DROP ROLE IF EXISTS "{migratorName}";
                """);
        }
    }

    private static PrimaryDatabaseConnectionOptions Options(
        NpgsqlConnectionStringBuilder administrator,
        string database,
        string username,
        string password,
        PrimaryDatabaseRole role) => new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = administrator.Host,
            Port = administrator.Port,
            Database = database,
            Username = username,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };

    private static ExploreDbContext CreateContext(PrimaryDatabaseConnectionOptions database)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        return new ExploreDbContext(options.Options);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertAuthorityAsync(
        NpgsqlConnection connection,
        string expectedUser,
        bool privileged)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT current_user::text, session_user::text, rolsuper, rolbypassrls,
                   rolcreatedb, rolcreaterole, rolreplication, rolinherit
            FROM pg_catalog.pg_roles WHERE rolname = current_user
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(expectedUser);
        await Assert.That(reader.GetString(1)).IsEqualTo(expectedUser);
        await Assert.That(reader.GetBoolean(2)).IsEqualTo(privileged);
        if (!privileged)
        {
            for (int column = 3; column <= 7; column++)
            {
                await Assert.That(reader.GetBoolean(column)).IsFalse();
            }
        }
    }

    private static async Task AssertRuntimePrivilegesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                has_database_privilege(current_user, current_database(), 'CREATE'),
                has_database_privilege(current_user, current_database(), 'TEMP'),
                EXISTS (
                    SELECT 1 FROM pg_catalog.pg_namespace
                    WHERE has_schema_privilege(current_user, oid, 'CREATE')),
                EXISTS (
                    SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname <> current_user AND pg_has_role(current_user, oid, 'SET')),
                EXISTS (
                    SELECT 1 FROM pg_catalog.pg_shdepend
                    WHERE refclassid = 'pg_catalog.pg_authid'::regclass
                      AND refobjid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user)
                      AND deptype = 'o'),
                has_table_privilege(current_user, 'islamu_event.event_sessions', 'SELECT'),
                has_table_privilege(current_user, 'islamu_event.event_sessions', 'INSERT'),
                has_table_privilege(current_user, 'islamu_event.ui_theme_presets', 'SELECT'),
                has_table_privilege(current_user, 'islamu_event.ui_theme_presets', 'INSERT'),
                has_table_privilege(current_user, 'islamu_event.ui_theme_presets', 'UPDATE'),
                has_table_privilege(current_user, 'islamu_event.ui_theme_presets', 'DELETE')
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        for (int column = 0; column <= 4; column++)
        {
            await Assert.That(reader.GetBoolean(column)).IsFalse();
        }
        for (int column = 5; column <= 7; column++)
        {
            await Assert.That(reader.GetBoolean(column)).IsTrue();
        }
        for (int column = 8; column <= 10; column++)
        {
            await Assert.That(reader.GetBoolean(column)).IsFalse();
        }
    }

    private static async Task AssertAdjustmentStateAsync(NpgsqlConnection connection, bool completed)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT table_entry.relrowsecurity, table_entry.relforcerowsecurity,
                EXISTS (
                    SELECT 1 FROM pg_catalog.pg_constraint
                    WHERE conrelid = table_entry.oid
                      AND conname = 'ex_event_session_room_no_overlap' AND contype = 'x'),
                EXISTS (
                    SELECT 1 FROM pg_catalog.pg_policy
                    WHERE polrelid = table_entry.oid AND polname = 'tenant_isolation'),
                table_entry.relowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user)
            FROM pg_catalog.pg_class AS table_entry
            WHERE table_entry.oid = 'islamu_event.event_sessions'::regclass
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        for (int column = 0; column <= 2; column++)
        {
            await Assert.That(reader.GetBoolean(column)).IsEqualTo(completed);
        }
        await Assert.That(reader.GetBoolean(3)).IsTrue();
        await Assert.That(reader.GetBoolean(4)).IsTrue();
    }

    private static async Task AssertMigrationHistoryAsync(DbContext context)
    {
        string[] known = context.Database.GetMigrations().ToArray();
        await Assert.That(known).IsNotEmpty();
        await Assert.That((await context.Database.GetAppliedMigrationsAsync()).ToArray())
            .IsEquivalentTo(known, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
