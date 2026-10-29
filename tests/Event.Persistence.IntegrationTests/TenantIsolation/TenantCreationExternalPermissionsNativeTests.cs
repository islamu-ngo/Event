using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>
/// Main-store External-profile permissions only. This fixture does not create an
/// external identity store or establish authentication/creation workflow authority.
/// </summary>
[ClassDataSource<TenantCreationExternalPermissionsNativeTests.ExternalProfileFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class TenantCreationExternalPermissionsNativeTests(
    TenantCreationExternalPermissionsNativeTests.ExternalProfileFixture fixture)
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task ExternalProfileInstallsAndReattestsWithoutChangingRuntimeAuthorityOrBusinessRows()
    {
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync(Token);
        await RequireIdentityAsync(migrator, fixture.NativeQueueEnrollment.MigratorLogin);
        string graphBefore = await GraphDigestAsync(migrator);
        string runtimeBefore = await RuntimePrivilegesDigestAsync(migrator);
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync(Token);
        await RequireIdentityAsync(runtime, fixture.NativeQueueEnrollment.RuntimeLogin);
        var runtimeConnector = (NpgsqlConnection)runtime.Database.GetDbConnection();
        int runtimeBackend = runtimeConnector.ProcessID;
        var runtimeLease = runtime.ContextId;

        // Commit the fresh image first. The second call must accept that complete
        // image without taking settlement away from the caller-owned transaction.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable, Token);
            var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
            var physical = transaction.GetDbTransaction();
            int backend = connector.ProcessID;
            var lease = migrator.ContextId;

            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.External, Token);

            await Assert.That(migrator.ContextId).IsEqualTo(lease);
            await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await RequireIdentityAsync(migrator, fixture.NativeQueueEnrollment.MigratorLogin);
            if (attempt == 0) await transaction.CommitAsync(Token);
            else await transaction.RollbackAsync(Token);
        }

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync(Token);
        await global.Database.OpenConnectionAsync(Token);
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable, Token))
        {
            var connector = (NpgsqlConnection)global.Database.GetDbConnection();
            var physical = transaction.GetDbTransaction();
            int backend = connector.ProcessID;
            var lease = global.ContextId;
            await RequireIdentityAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
            await PostgresTenantCreationPermissions.RequireCurrentAsync(
                global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.External, Token);
            await PostgresTenantCreationPermissions.RequireCurrentAsync(
                global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.External, Token);

            await Assert.That(global.ContextId).IsEqualTo(lease);
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.IsTenantFilterBypassed).IsFalse();
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            await RequireIdentityAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
            await transaction.RollbackAsync(Token);
        }

        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(graphBefore);
        await Assert.That(await RuntimePrivilegesDigestAsync(migrator)).IsEqualTo(runtimeBefore);
        await RequireIdentityAsync(runtime, fixture.NativeQueueEnrollment.RuntimeLogin);
        await Assert.That(runtime.ContextId).IsEqualTo(runtimeLease);
        await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(runtimeConnector);
        await Assert.That(runtimeConnector.ProcessID).IsEqualTo(runtimeBackend);
        await Assert.That(runtime.TenantFilterTenantId).IsNull();
        await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
        await Assert.That(runtime.ChangeTracker.Entries().Any()).IsFalse();
    }

    private static async Task RequireIdentityAsync(ExploreDbContext context, string login)
    {
        await using var command = Command(context, """
            SELECT current_user=session_user AND session_user=@login
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
            """);
        command.Parameters.Add(new NpgsqlParameter("login", login));
        await Assert.That(await command.ExecuteScalarAsync(Token)).IsEqualTo(true);
    }

    private static async Task<string> GraphDigestAsync(ExploreDbContext migrator)
    {
        var names = new HumanPrincipalSourceSql(migrator);
        string[] projections = new[] { typeof(Tenant), typeof(TenantSettingsDocument), typeof(EventDiscoveryRevision) }
            .Select(type => $"(SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY r.{names.Column(type, "Id")})::text,'[]')) FROM {names.Table(type)} r)")
            .ToArray();
        await using var command = Command(migrator, "SELECT " + string.Join(" || ':' || ", projections));
        return (string)(await command.ExecuteScalarAsync(Token)
            ?? throw new InvalidOperationException("The independent main-store observation is missing."));
    }

    private async Task<string> RuntimePrivilegesDigestAsync(ExploreDbContext migrator)
    {
        // Catalog privilege checks inspect no application or private identity rows.
        await using var command = Command(migrator, """
            WITH rights AS (
                SELECT relation.oid,0 AS attribute,privilege.name,
                    pg_catalog.has_table_privilege(@runtime,relation.oid,privilege.name) AS allowed
                FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid=relation.relnamespace
                CROSS JOIN unnest(ARRAY['SELECT','INSERT','UPDATE','DELETE','TRUNCATE','REFERENCES','TRIGGER','MAINTAIN']) privilege(name)
                WHERE namespace.nspname=@schema AND relation.relkind IN ('r','p','v','m','f')
                UNION ALL
                SELECT relation.oid,column_definition.attnum,privilege.name,
                    pg_catalog.has_column_privilege(@runtime,relation.oid,column_definition.attnum,privilege.name)
                FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid=relation.relnamespace
                JOIN pg_catalog.pg_attribute column_definition ON column_definition.attrelid=relation.oid
                CROSS JOIN unnest(ARRAY['SELECT','INSERT','UPDATE','REFERENCES']) privilege(name)
                WHERE namespace.nspname=@schema AND relation.relkind IN ('r','p','v','m','f')
                    AND column_definition.attnum<>0 AND NOT column_definition.attisdropped)
            SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(rights)
                ORDER BY oid,attribute,name)::text,'[]')) FROM rights
            """);
        command.Parameters.Add(new NpgsqlParameter("runtime", fixture.NativeQueueEnrollment.RuntimeLogin));
        command.Parameters.Add(new NpgsqlParameter("schema", new HumanPrincipalSourceSql(migrator).Schema));
        return (string)(await command.ExecuteScalarAsync(Token)
            ?? throw new InvalidOperationException("The Runtime privilege observation is missing."));
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    public sealed class ExternalProfileFixture : PostgreSqlContainerFixture
    {
        public ExternalProfileFixture() : base(IdentityDatabaseTopology.External)
        {
        }
    }
}
