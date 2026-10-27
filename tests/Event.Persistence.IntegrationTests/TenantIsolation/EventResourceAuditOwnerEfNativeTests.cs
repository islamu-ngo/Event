using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[NotInParallel]
public sealed class EventResourceAuditOwnerEfNativeTests
{
    [Test]
    public async Task OriginalMigratorInstallsOnlyGlobalAuditTenantKeyReads()
    {
        await using var fixture = new PostgreSqlContainerFixture();
        await fixture.InitializeAsync();
        await using var migrator = Migrator(fixture);
        var audit = Mapping<EventResourceAuditEntry>(migrator);
        string runtimeBefore = await RuntimeAuthorityAsync(migrator, audit.Table, fixture.NativeQueueEnrollment.RuntimeLogin);
        await InstallAsync(migrator, fixture);
        await Assert.That(await RuntimeAuthorityAsync(migrator, audit.Table, fixture.NativeQueueEnrollment.RuntimeLogin))
            .IsEqualTo(runtimeBefore);

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var connection = global.Database.GetDbConnection();
        var contextId = global.ContextId;
        int backend = ((NpgsqlConnection)connection).ProcessID;
        await AssertSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
        await PostgresEventResourceAuditReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        global.RequireBoundTenantLease();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await Assert.That(global.ContextId).IsEqualTo(contextId);
        await Assert.That(ReferenceEquals(connection, global.Database.GetDbConnection())).IsTrue();
        await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);

        var tenant = Mapping<Tenant>(global);
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT pg_catalog.has_column_privilege(current_user,@audit,@tenant_column,'SELECT')
                    AND pg_catalog.has_column_privilege(current_user,@tenant,@id_column,'SELECT')
                    AND NOT pg_catalog.has_table_privilege(current_user,@audit,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid=pg_catalog.to_regclass(@audit) AND a.attnum>0 AND NOT a.attisdropped
                          AND ((a.attname<>@tenant_column AND pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT'))
                            OR pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'INSERT,UPDATE,REFERENCES')
                            OR pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')))
                """;
            query.Parameters.Add(new NpgsqlParameter("audit", audit.Table));
            query.Parameters.Add(new NpgsqlParameter("tenant", tenant.Table));
            query.Parameters.Add(new NpgsqlParameter("tenant_column", audit.Columns[nameof(EventResourceAuditEntry.TenantId)]));
            query.Parameters.Add(new NpgsqlParameter("id_column", tenant.Columns[nameof(Tenant.Id)]));
            await Assert.That(await query.ExecuteScalarAsync()).IsEqualTo(true);
        }
        await AssertDeniedAsync(global,
            $"SELECT {Quote(audit.Columns[nameof(EventResourceAuditEntry.ResponsibleManagerUserId)])} FROM {audit.Table} WHERE false");
        await AssertDeniedAsync(global, $"SELECT ctid FROM {audit.Table} WHERE false");
        await AssertDeniedAsync(global, $"DELETE FROM {audit.Table} WHERE false");
        try
        {
            await SetTenantSettingAsync(global, Guid.CreateVersion7().ToString());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresEventResourceAuditReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await SetTenantSettingAsync(global, "");
        }
        await PostgresEventResourceAuditReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await AssertSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
        await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
    }

    [Test]
    public async Task GlobalAdmissionRejectsTheExactAuditReadPolicyWithoutUsing()
    {
        await using var fixture = new PostgreSqlContainerFixture();
        await fixture.InitializeAsync();
        await using var migrator = Migrator(fixture);
        await InstallAsync(migrator, fixture);
        var audit = Mapping<EventResourceAuditEntry>(migrator);
        const string policy = "event_resource_audit_global_read";
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        async Task RequireHealthyAsync()
        {
            await using var global = await factory.CreateDbContextAsync();
            await global.Database.OpenConnectionAsync();
            await AssertSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
            await PostgresEventResourceAuditReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        }
        await RequireHealthyAsync();
        await migrator.Database.OpenConnectionAsync();
        string originalExpression;
        await using (var original = migrator.Database.GetDbConnection().CreateCommand())
        {
            original.CommandText = """
                SELECT pg_catalog.pg_get_expr(polqual,polrelid)
                FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table) AND polname=@policy
                """;
            original.Parameters.Add(new NpgsqlParameter("table", audit.Table));
            original.Parameters.Add(new NpgsqlParameter("policy", policy));
            originalExpression = (string)(await original.ExecuteScalarAsync())!;
        }
        Task ReplacePolicyAsync(string? expression) =>
            migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await migrator.Database.BeginTransactionAsync();
                await AssertSessionAsync(migrator, fixture.NativeQueueEnrollment.MigratorLogin);
                string condition = expression is null ? "" : $" USING ({expression})";
                await migrator.Database.ExecuteSqlRawAsync($"""
                    DROP POLICY {Quote(policy)} ON {audit.Table};
                    CREATE POLICY {Quote(policy)} ON {audit.Table} AS PERMISSIVE FOR SELECT
                        TO {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}{condition};
                    """);
                await transaction.CommitAsync();
            });
        try
        {
            await ReplacePolicyAsync(null);
            await using (var injected = migrator.Database.GetDbConnection().CreateCommand())
            {
                injected.CommandText = """
                    SELECT polqual IS NULL AND polwithcheck IS NULL AND polcmd='r' AND polpermissive
                        AND polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@global)]
                    FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table) AND polname=@policy
                    """;
                injected.Parameters.Add(new NpgsqlParameter("table", audit.Table));
                injected.Parameters.Add(new NpgsqlParameter("policy", policy));
                injected.Parameters.Add(new NpgsqlParameter("global", fixture.NativeQueueEnrollment.GlobalLogin));
                await Assert.That(await injected.ExecuteScalarAsync()).IsEqualTo(true);
            }
            // Check generic connection admission, not only the capability-specific
            // readiness method that would independently notice the missing predicate.
            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var global = await factory.CreateDbContextAsync();
                await global.Database.OpenConnectionAsync();
            });
            await Assert.That(rejected.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
        }
        finally
        {
            // Restore only the exact policy captured after genuine healthy admission.
            await ReplacePolicyAsync(originalExpression);
        }
        await RequireHealthyAsync();
    }

    [Test]
    public async Task OwnerDiscoverySucceedsWithoutTheRetiredBusinessFunction()
    {
        await using var fixture = new PostgreSqlContainerFixture();
        await fixture.InitializeAsync();
        await using var migrator = Migrator(fixture);
        await InstallAsync(migrator, fixture);
        var audit = Mapping<EventResourceAuditEntry>(migrator);
        string function = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(audit.Name + "_owners", audit.Schema) + "(uuid,integer)";
        // This fixture owns its disposable database. Remove only the obsolete routine;
        // never reinstall it or replace its business logic to make discovery pass.
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync();
            await migrator.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS {function}");
            await transaction.CommitAsync();
        });
        await migrator.Database.OpenConnectionAsync();
        await using (var absent = migrator.Database.GetDbConnection().CreateCommand())
        {
            absent.CommandText = "SELECT pg_catalog.to_regprocedure(@function) IS NULL";
            absent.Parameters.Add(new NpgsqlParameter("function", function));
            await Assert.That(await absent.ExecuteScalarAsync()).IsEqualTo(true);
        }
        long before = await AuditCountAsync(migrator, audit.Table);
        await Assert.That(before).IsEqualTo(0);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var repository = new EventResourceAuditOwnerDiscoveryRepository(factory, fixture.NativeQueueEnrollment);
        var page = await repository.GetTenantIdsWithAuditAsync(null, 1, CancellationToken.None);
        await Assert.That(page.Count).IsEqualTo(0);
        await Assert.That(await AuditCountAsync(migrator, audit.Table)).IsEqualTo(before);
    }

    private static ExploreDbContext Migrator(PostgreSqlContainerFixture fixture)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private static Task InstallAsync(ExploreDbContext context, PostgreSqlContainerFixture fixture) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var connection = context.Database.GetDbConnection();
            DbTransaction physical = transaction.GetDbTransaction();
            var contextId = context.ContextId;
            int backend = ((NpgsqlConnection)connection).ProcessID;
            await AssertSessionAsync(context, fixture.NativeQueueEnrollment.MigratorLogin);
            await PostgresEventResourceAuditReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresEventResourceAuditReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(context.ContextId).IsEqualTo(contextId);
            await Assert.That(ReferenceEquals(connection, context.Database.GetDbConnection())).IsTrue();
            await Assert.That(ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())).IsTrue();
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await AssertSessionAsync(context, fixture.NativeQueueEnrollment.MigratorLogin);
            await transaction.CommitAsync();
        });

    private static async Task AssertSessionAsync(ExploreDbContext context, string login)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_user=@login AND session_user=@login";
        command.Parameters.Add(new NpgsqlParameter("login", login));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<string> RuntimeAuthorityAsync(ExploreDbContext context, string table, string runtime)
    {
        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COALESCE(string_agg(a.attnum::text || ':' ||
                pg_catalog.has_column_privilege(@runtime,a.attrelid,a.attnum,'SELECT')::text || ':' ||
                pg_catalog.has_column_privilege(@runtime,a.attrelid,a.attnum,'INSERT')::text || ':' ||
                pg_catalog.has_column_privilege(@runtime,a.attrelid,a.attnum,'UPDATE')::text, ',' ORDER BY a.attnum),'')
                || ':' || pg_catalog.has_table_privilege(@runtime,@table,'DELETE')::text
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0 AND NOT a.attisdropped
            """;
        command.Parameters.Add(new NpgsqlParameter("runtime", runtime));
        command.Parameters.Add(new NpgsqlParameter("table", table));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> AuditCountAsync(ExploreDbContext context, string table)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertDeniedAsync(ExploreDbContext context, string sql)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        });
        await Assert.That(exception.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task SetTenantSettingAsync(ExploreDbContext context, string tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,false)";
        command.Parameters.Add(new NpgsqlParameter("tenant", tenant));
        await command.ExecuteScalarAsync();
    }

    private static (string Table, string Name, string Schema, Dictionary<string, string> Columns) Mapping<T>(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        string name = entity.GetTableName()!;
        string schema = entity.GetSchema()!;
        var mapping = StoreObjectIdentifier.Table(name, schema);
        return (context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), name, schema,
            entity.GetProperties().ToDictionary(property => property.Name, property => property.GetColumnName(mapping)!));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
