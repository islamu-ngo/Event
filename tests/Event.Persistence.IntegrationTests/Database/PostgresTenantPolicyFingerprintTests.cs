using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresTenantPolicyFingerprintTests(PostgreSqlContainerFixture fixture)
{
    private const string TenantExpression =
        "nullif(current_setting('app.current_tenant_id', true), '')::uuid";

    [Test]
    public async Task InstalledDirectPolicyCommentContainsContractHashAndStoredTreeHashes()
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture);

        await AssertInstalledStampAsync(database, $"\"tenant_id\" = {TenantExpression}");
    }

    [Test]
    public async Task ReinstallingUnchangedPolicyPreservesCompleteStamp()
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture);
        var before = await database.ReadStampAsync();

        await database.InstallAsync();

        await Assert.That(await database.ReadStampAsync()).IsEqualTo(before);
        await AssertInstalledStampAsync(database, $"\"tenant_id\" = {TenantExpression}");
    }

    [Test]
    [Arguments("USING")]
    [Arguments("WITH CHECK")]
    public async Task AlteredPolicyTreeLeavesStoredCommentMismatched(string clause)
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture);
        var before = await database.ReadStampAsync();
        await AssertInstalledStampAsync(database, $"\"tenant_id\" = {TenantExpression}");

        await PolicyDatabase.ExecuteAsync(database.Migration,
            $"ALTER POLICY tenant_isolation ON {database.Table} {clause} (true)");

        var after = await database.ReadStampAsync();
        await Assert.That(after.Comment).IsEqualTo(before.Comment);
        await Assert.That(after.Comment).IsNotEqualTo(database.Prefix(
            $"\"tenant_id\" = {TenantExpression}") + after.UsingHash + ":" + after.CheckHash);
        await Assert.That(clause == "USING" ? after.UsingHash : after.CheckHash)
            .IsNotEqualTo(clause == "USING" ? before.UsingHash : before.CheckHash);
        await Assert.That(clause == "USING" ? after.CheckHash : after.UsingHash)
            .IsEqualTo(clause == "USING" ? before.CheckHash : before.UsingHash);
        // The stamp exposes drift to native consumers; this generator is an installer,
        // not a drift-rejection boundary, so deliberately do not reinstall here.
    }

    [Test]
    public async Task StampedPolicyPreservesRuntimeTenantReadAndWriteBoundaries()
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture);
        await using var runtime = await database.OpenRuntimeAsync(database.TenantA);
        await Assert.That(await PolicyDatabase.ScalarAsync<bool>(runtime, """
            SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_catalog.pg_roles
            WHERE rolname = current_user
            """)).IsTrue();
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("1");
        await Assert.That(await PolicyDatabase.ExecuteAsync(runtime,
            $"UPDATE {database.Table} SET probe_id = 20 WHERE probe_id = 2")).IsEqualTo(0);
        await Assert.That(await PolicyDatabase.ExecuteAsync(runtime,
            $"INSERT INTO {database.Table} VALUES (3, '{database.TenantA}')")).IsEqualTo(1);

        var insert = await Assert.ThrowsAsync<PostgresException>(() =>
            PolicyDatabase.ExecuteAsync(runtime,
                $"INSERT INTO {database.Table} VALUES (4, '{database.TenantB}')"));
        await Assert.That(insert!.SqlState).IsEqualTo("42501");
        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            PolicyDatabase.ExecuteAsync(runtime,
                $"UPDATE {database.Table} SET tenant_id = '{database.TenantB}' WHERE probe_id = 1"));
        await Assert.That(update!.SqlState).IsEqualTo("42501");

        await database.BindTenantAsync(runtime, database.TenantB);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("2");
        await database.BindTenantAsync(runtime, null);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task NullableTenantPolicyStampsItsContractAndRetainsSharedRows()
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture, allowsNullTenant: true);
        await PolicyDatabase.ExecuteAsync(database.Migration,
            $"INSERT INTO {database.Table} VALUES (3, NULL)");
        await AssertInstalledStampAsync(database,
            $"(\"tenant_id\" IS NULL OR \"tenant_id\" = {TenantExpression})");
        await using var runtime = await database.OpenRuntimeAsync(database.TenantA);

        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("1,3");
        await database.BindTenantAsync(runtime, database.TenantB);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("2,3");
        await database.BindTenantAsync(runtime, null);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("3");
    }

    [Test]
    public async Task MappedParentPolicyStampsJoinContractAndEnforcesParentTenant()
    {
        await using var original = fixture.CreateDbContext();
        var mapped = PostgresTenantRowLevelSecurityModel.GetTenantTables(original.Model)
            .First(table => table.ParentJoin is { ParentAllowsNullTenant: false });
        await using var database = await PolicyDatabase.CreateAsync(fixture, mappedParent: mapped);
        var join = database.Metadata.ParentJoin!;
        string parent = PolicyDatabase.Quote(join.ParentSchema!) + "." +
            PolicyDatabase.Quote(join.ParentTableName);
        string condition = $"EXISTS (SELECT 1 FROM {parent} AS \"p\" WHERE " +
            $"\"p\".{PolicyDatabase.Quote(join.ParentKeyColumn)} = {database.Table}." +
            $"{PolicyDatabase.Quote(join.ForeignKeyColumn)} AND " +
            $"\"p\".{PolicyDatabase.Quote(join.ParentTenantIdColumn)} = {TenantExpression})";
        await AssertInstalledStampAsync(database, condition);
        await using var runtime = await database.OpenRuntimeAsync(database.TenantA);

        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("1");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PolicyDatabase.ExecuteAsync(runtime,
                $"INSERT INTO {database.Table} VALUES (3, '{database.TenantB}')"));
        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await database.BindTenantAsync(runtime, database.TenantB);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo("2");
        await database.BindTenantAsync(runtime, null);
        await Assert.That(await database.VisibleIdsAsync(runtime)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AlternateSearchPathCannotRedirectExplicitlyQualifiedPolicyTarget()
    {
        await using var database = await PolicyDatabase.CreateAsync(fixture);
        var before = await database.ReadStampAsync();
        string decoy = PolicyDatabase.Quote(database.Schema + "_decoy");
        await PolicyDatabase.ExecuteAsync(database.Migration, $"""
            CREATE SCHEMA {decoy};
            CREATE TABLE {decoy}."probe" (probe_id integer PRIMARY KEY, tenant_id uuid);
            SET search_path TO {decoy}, pg_catalog;
            """);
        try
        {
            await database.InstallAsync();

            await Assert.That(await database.ReadStampAsync()).IsEqualTo(before);
            await Assert.That(await PolicyDatabase.ScalarAsync<long>(database.Migration, $"""
                SELECT count(*) FROM pg_catalog.pg_policy
                WHERE polrelid = '{decoy}."probe"'::regclass
                """)).IsEqualTo(0L);
            await Assert.That(await PolicyDatabase.ScalarAsync<bool>(database.Migration, $"""
                SELECT relrowsecurity FROM pg_catalog.pg_class
                WHERE oid = '{decoy}."probe"'::regclass
                """)).IsFalse();
        }
        finally
        {
            await PolicyDatabase.ExecuteAsync(database.Migration,
                $"SET search_path TO pg_catalog; DROP SCHEMA {decoy} CASCADE");
        }
    }

    private static async Task AssertInstalledStampAsync(PolicyDatabase database, string condition)
    {
        var stamp = await database.ReadStampAsync();
        await Assert.That(stamp.Comment)
            .IsEqualTo(database.Prefix(condition) + stamp.UsingHash + ":" + stamp.CheckHash);
        await Assert.That(stamp.UsingHash.Length).IsEqualTo(32);
        await Assert.That(stamp.CheckHash.Length).IsEqualTo(32);
    }

    private sealed class PolicyDatabase(
        PostgreSqlContainerFixture fixture, ExploreDbContext migration, string schema,
        TenantTableMetadata metadata) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;
        internal string Schema { get; } = schema;
        internal TenantTableMetadata Metadata { get; } = metadata;
        internal string Table => Quote(Schema) + "." + Quote(Metadata.TableName);
        internal Guid TenantA { get; } = Guid.CreateVersion7();
        internal Guid TenantB { get; } = Guid.CreateVersion7();

        internal static async Task<PolicyDatabase> CreateAsync(
            PostgreSqlContainerFixture fixture, bool allowsNullTenant = false,
            TenantTableMetadata? mappedParent = null)
        {
            string schema = "policy_stamp_" + Guid.CreateVersion7().ToString("N");
            var metadata = mappedParent is null
                ? new TenantTableMetadata("probe", schema, "tenant_id", allowsNullTenant)
                : mappedParent with
                {
                    Schema = schema,
                    ParentJoin = mappedParent.ParentJoin! with { ParentSchema = schema }
                };
            var migration = CreateContext(fixture, PrimaryDatabaseRole.Migrator, schema);
            var database = new PolicyDatabase(fixture, migration, schema, metadata);
            try
            {
                await migration.Database.OpenConnectionAsync();
                string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
                await ExecuteAsync(migration,
                    $"CREATE SCHEMA {Quote(schema)}; GRANT USAGE ON SCHEMA {Quote(schema)} TO {runtime}");
                string tenantColumn = Quote(metadata.TenantIdColumn ?? metadata.ParentJoin!.ForeignKeyColumn);
                if (metadata.ParentJoin is { } join)
                {
                    string parent = Quote(schema) + "." + Quote(join.ParentTableName);
                    await ExecuteAsync(migration, $"""
                        CREATE TABLE {parent} ({Quote(join.ParentKeyColumn)} uuid PRIMARY KEY,
                            {Quote(join.ParentTenantIdColumn)} uuid NOT NULL);
                        INSERT INTO {parent} VALUES
                            ('{database.TenantA}', '{database.TenantA}'),
                            ('{database.TenantB}', '{database.TenantB}');
                        GRANT SELECT ON {parent} TO {runtime};
                        """);
                }
                await ExecuteAsync(migration, $"""
                    CREATE TABLE {database.Table} (probe_id integer PRIMARY KEY,
                        {tenantColumn} uuid {(allowsNullTenant ? "" : "NOT NULL")});
                    INSERT INTO {database.Table} VALUES
                        (1, '{database.TenantA}'), (2, '{database.TenantB}');
                    GRANT SELECT, INSERT, UPDATE, DELETE ON {database.Table} TO {runtime};
                    """);
                await database.InstallAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        internal Task<int> InstallAsync() => ExecuteAsync(Migration,
            PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(Metadata));

        internal string Prefix(string condition) => "event-tenant-policy:v2:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(condition))) + ":";

        internal async Task<(string Comment, string UsingHash, string CheckHash)> ReadStampAsync()
        {
            await using var command = Migration.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                SELECT pg_catalog.obj_description(p.oid, 'pg_policy'),
                    pg_catalog.md5(p.polqual::text), pg_catalog.md5(p.polwithcheck::text)
                FROM pg_catalog.pg_policy p
                WHERE p.polrelid = '{Table}'::regclass AND p.polname = 'tenant_isolation'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        internal async Task<ExploreDbContext> OpenRuntimeAsync(Guid tenant)
        {
            var context = CreateContext(fixture, PrimaryDatabaseRole.Runtime, Schema);
            try
            {
                await context.Database.OpenConnectionAsync();
                await BindTenantAsync(context, tenant);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task BindTenantAsync(ExploreDbContext context, Guid? tenant)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, false)";
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString() ?? string.Empty));
            await command.ExecuteScalarAsync();
        }

        internal Task<string> VisibleIdsAsync(ExploreDbContext runtime) => ScalarAsync<string>(runtime,
            $"SELECT coalesce(string_agg(probe_id::text, ',' ORDER BY probe_id), '') FROM {Table}");

        internal static async Task<int> ExecuteAsync(ExploreDbContext context, string sql)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        }

        internal static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PrimaryDatabaseRole role, string schema)
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(
                options, fixture.GetNativeQueueOptions(role, schema));
            return new ExploreDbContext(options.Options);
        }

        internal static string Quote(string identifier) =>
            "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(Migration, $"DROP SCHEMA IF EXISTS {Quote(Schema)} CASCADE");
            }
            finally
            {
                await Migration.DisposeAsync();
            }
        }
    }
}
