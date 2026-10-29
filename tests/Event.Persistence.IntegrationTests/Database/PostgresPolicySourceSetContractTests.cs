using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresPolicySourceSetContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task InstallationAndReinstallationPreserveEnrolledGlobalAcquisition(int purpose)
    {
        await using var database = await SourceDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await database.AcquireAsync(caller, purpose);

        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(typeof(RolePermission))]
    [Arguments(typeof(SystemSetting))]
    [Arguments(typeof(TenantSetting))]
    public async Task GlobalAcquisitionBlocksIndependentSourceInsertAndRollbackReleasesIt(Type source)
    {
        await using var database = await SourceDatabase.CreateAsync(fixture);
        await using var global = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await database.AcquireAsync(global, 2);
        await Assert.That(await database.CountAsync(source)).IsEqualTo(0L);

        // Both sessions are already open and the acquisition has completed. The database,
        // not a scheduling delay, bounds the independently executed statement's lock wait.
        await SourceDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.InsertAsync(writer, source));
        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await Assert.That(await database.CountAsync(source)).IsEqualTo(0L);

        await transaction.RollbackAsync();
        await Assert.That(await database.InsertAsync(writer, source)).IsEqualTo(1);
        await Assert.That(await database.CountAsync(source)).IsEqualTo(1L);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2)]
    [Arguments(PrimaryDatabaseRole.Runtime, 3)]
    [Arguments(PrimaryDatabaseRole.Global, 1)]
    [Arguments(PrimaryDatabaseRole.Global, 4)]
    public async Task EnrolledLoginCannotAcquireAnotherPurpose(PrimaryDatabaseRole role, int purpose)
    {
        await using var database = await SourceDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime || purpose == 1 ? Guid.CreateVersion7() : null;
        await using (var setting = SourceDatabase.Command(caller,
            "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, true)"))
        {
            setting.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString() ?? ""));
            await setting.ExecuteScalarAsync();
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.AcquireAsync(caller, purpose, tenant));
        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("policy_source_set_caller_mismatch");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("trigger")]
    [Arguments("body")]
    public async Task ReinstallationRejectsDriftWithoutRepairingIt(string drift)
    {
        await using var database = await SourceDatabase.CreateAsync(fixture);
        string acquire = PostgresPolicySourceSetContract.AcquireFunctionSql(database.Migration);
        const string forgedBody = "BEGIN RETURN; END;";
        string change = drift switch
        {
            "trigger" => $"""
                ALTER TABLE {database.Table(typeof(RolePermission))}
                    DISABLE TRIGGER policy_source_set_write;
                """,
            "body" => $"""
                CREATE OR REPLACE FUNCTION {acquire}(p_purpose integer, p_tenant uuid)
                    RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                    SET search_path = pg_catalog AS $body${forgedBody}$body$;
                COMMENT ON FUNCTION {acquire}(integer,uuid) IS
                    'policy-source-set:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(forgedBody)))}';
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await SourceDatabase.ExecuteAsync(database.Migration, change);
        string observation = drift == "trigger"
            ? $"""
                SELECT pg_catalog.pg_get_triggerdef(oid) || ':' || tgenabled::text
                FROM pg_catalog.pg_trigger
                WHERE tgrelid = '{database.Table(typeof(RolePermission))}'::regclass
                    AND tgname = 'policy_source_set_write'
                """
            : $"""
                SELECT pg_catalog.pg_get_functiondef(oid) || ':' || pg_catalog.obj_description(oid, 'pg_proc')
                FROM pg_catalog.pg_proc WHERE oid = '{acquire}(integer,uuid)'::regprocedure
                """;
        string before = await SourceDatabase.ScalarAsync<string>(database.Migration, observation);

        var error = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);

        await Assert.That(error!.SqlState).IsEqualTo("55000");
        await Assert.That(error.MessageText).IsEqualTo(drift == "trigger"
            ? "policy_source_set_trigger_drift" : "policy_source_set_body_drift");
        await Assert.That(await SourceDatabase.ScalarAsync<string>(database.Migration, observation)).IsEqualTo(before);
    }

    private sealed class SourceDatabase(
        PostgreSqlContainerFixture fixture, ExploreDbContext migration, string schema) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;

        internal static async Task<SourceDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            string schema = "policy_sources_" + Guid.CreateVersion7().ToString("N");
            var migration = CreateContext(fixture, PrimaryDatabaseRole.Migrator, schema);
            var database = new SourceDatabase(fixture, migration, schema);
            try
            {
                await migration.Database.OpenConnectionAsync();
                await ExecuteAsync(migration, $"CREATE SCHEMA {Quote(schema)}");
                await using var original = CreateContext(fixture, PrimaryDatabaseRole.Migrator);
                foreach (Type source in new[] { typeof(RolePermission), typeof(SystemSetting), typeof(TenantSetting) })
                {
                    var entity = original.Model.FindEntityType(source)!;
                    string originalTable = Quote(entity.GetSchema()!) + "." + Quote(entity.GetTableName()!);
                    // Copy the migrated source columns, defaults, checks and indexes, not unrelated
                    // foreign-key graphs or data. These are real permanent PostgreSQL relations.
                    await ExecuteAsync(migration, $"""
                        CREATE TABLE {database.Table(source)} (LIKE {originalTable} INCLUDING ALL);
                        GRANT SELECT, INSERT ON {database.Table(source)} TO {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)};
                        """);
                }
                await ExecuteAsync(migration,
                    $"GRANT USAGE ON SCHEMA {Quote(schema)} TO {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}");
                await database.InstallAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        internal Task InstallAsync() =>
            PostgresPolicySourceSetContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

        internal async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role)
        {
            var context = CreateContext(fixture, role, schema);
            try
            {
                await context.Database.OpenConnectionAsync();
                await ExecuteAsync(context, "SELECT pg_catalog.set_config('app.current_tenant_id', '', false)");
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task AcquireAsync(ExploreDbContext context, int purpose, Guid? tenant = null)
        {
            await using var command = Command(context,
                $"SELECT {PostgresPolicySourceSetContract.AcquireFunctionSql(Migration)}(@purpose, @tenant)");
            command.Parameters.Add(new NpgsqlParameter("purpose", purpose));
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlTypes.NpgsqlDbType.Uuid)
            {
                Value = (object?)tenant ?? DBNull.Value
            });
            await command.ExecuteNonQueryAsync();
        }

        internal string Table(Type source) =>
            Quote(schema) + "." + Quote(Migration.Model.FindEntityType(source)!.GetTableName()!);

        private string Column(Type source, string property)
        {
            var entity = Migration.Model.FindEntityType(source)!;
            return Quote(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }

        internal Task<long> CountAsync(Type source) =>
            ScalarAsync<long>(Migration, $"SELECT count(*) FROM {Table(source)}");

        internal async Task<int> InsertAsync(ExploreDbContext writer, Type source)
        {
            string sql;
            if (source == typeof(RolePermission))
            {
                sql = $"""
                    INSERT INTO {Table(source)} ({Column(source, nameof(RolePermission.RoleId))},
                        {Column(source, nameof(RolePermission.PermissionId))}) VALUES (1, 1)
                    """;
            }
            else if (source == typeof(SystemSetting))
            {
                sql = $"""
                    INSERT INTO {Table(source)} ({Column(source, nameof(SystemSetting.Id))},
                        {Column(source, nameof(SystemSetting.SettingKey))}, {Column(source, nameof(SystemSetting.Value))},
                        {Column(source, nameof(SystemSetting.SettingValueTypeId))})
                    VALUES (@id, @key, 'null', 0)
                    """;
            }
            else if (source == typeof(TenantSetting))
            {
                sql = $"""
                    INSERT INTO {Table(source)} ({Column(source, nameof(TenantSetting.Id))},
                        {Column(source, nameof(TenantSetting.TenantId))}, {Column(source, nameof(TenantSetting.SettingKey))},
                        {Column(source, nameof(TenantSetting.Value))})
                    VALUES (@id, @tenant, @key, 'null')
                    """;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }
            await using var command = Command(writer, sql);
            if (source != typeof(RolePermission))
            {
                command.Parameters.Add(new NpgsqlParameter("id", Guid.CreateVersion7()));
                command.Parameters.Add(new NpgsqlParameter("key", GovernanceSettingKeys.Security.AuthorizationProvider));
                if (source == typeof(TenantSetting))
                    command.Parameters.Add(new NpgsqlParameter("tenant", Guid.CreateVersion7()));
            }
            return await command.ExecuteNonQueryAsync();
        }

        internal static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = sql;
            return command;
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

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PrimaryDatabaseRole role, string? schema = null)
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role, schema));
            return new ExploreDbContext(builder.Options);
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(Migration, $"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE");
            }
            finally
            {
                await Migration.DisposeAsync();
            }
        }
    }
}
