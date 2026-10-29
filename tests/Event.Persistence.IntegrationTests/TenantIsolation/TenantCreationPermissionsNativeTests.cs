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

/// <summary>Storage readiness only; no human authorization or creator graph is fabricated.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed partial class TenantCreationPermissionsNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task InstalledCorePermissionsRetainTheOriginalGlobalOwnerWithoutEffects()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;

        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        await Assert.That(global.ContextId).IsEqualTo(lease);
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        await transaction.RollbackAsync();
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    [Test]
    public async Task UnexpectedDocumentPayloadTriggerRejectsTheOriginalGlobalOwnerBeforeEffects()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string document = names.Table(typeof(TenantSettingsDocument));
        string payload = names.Column(typeof(TenantSettingsDocument), nameof(TenantSettingsDocument.PayloadJson));
        string name = "creator_payload_attack_" + Guid.CreateVersion7().ToString("N");
        string routine = names.Name(name);
        string trigger = names.Quote(name);

        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;

        try
        {
            // Commit test DDL so the independently authenticated Global session sees it.
            // The replacement remains a valid JSON object and does not violate document constraints.
            await using (var setup = await migrator.Database.BeginTransactionAsync())
            {
                await ExecuteAsync(migrator, $"""
                    CREATE FUNCTION {routine}() RETURNS trigger LANGUAGE plpgsql AS $attack$
                    BEGIN
                        NEW.{payload} := pg_catalog.jsonb_build_object('unexpected_payload', true);
                        RETURN NEW;
                    END;
                    $attack$;
                    CREATE TRIGGER {trigger} BEFORE INSERT ON {document}
                        FOR EACH ROW EXECUTE FUNCTION {routine}();
                    """);
                await setup.CommitAsync();
            }
            await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                try
                {
                    var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
                    await Assert.That(denied.Message).IsEqualTo("tenant_creation_permissions_not_ready");
                    await Assert.That(global.ContextId).IsEqualTo(lease);
                    await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
                    await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                    await Assert.That(connector.ProcessID).IsEqualTo(backend);
                    await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
        }
        finally
        {
            // These exact unique objects belong only to this test, not an installed protocol.
            await ExecuteAsync(migrator, $"DROP TRIGGER IF EXISTS {trigger} ON {document}; DROP FUNCTION IF EXISTS {routine}();");
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
    }

    private async Task InstallAsync(ExploreDbContext migrator)
    {
        await migrator.Database.OpenConnectionAsync();
        await using var transaction = await migrator.Database.BeginTransactionAsync();
        var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
        int backend = connector.ProcessID;
        await using (var identity = Command(migrator, "SELECT current_user=session_user AND session_user=@login"))
        {
            identity.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.MigratorLogin));
            await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
        }
        await PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await transaction.CommitAsync();
    }

    private async Task RequireGlobalIdentityAsync(ExploreDbContext context)
    {
        await using var command = Command(context, """
            SELECT current_user=session_user AND session_user=@login
                AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
            """);
        command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<string> GraphDigestAsync(ExploreDbContext migrator)
    {
        var names = new HumanPrincipalSourceSql(migrator);
        string[] projections = new[] { typeof(Tenant), typeof(TenantSettingsDocument), typeof(EventDiscoveryRevision) }
            .Select(type => $"(SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY r.{names.Column(type, "Id")})::text,'[]')) FROM {names.Table(type)} r)")
            .ToArray();
        await using var command = Command(migrator, "SELECT " + string.Join(" || ':' || ", projections));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The independent graph observation is missing."));
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private static async Task ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync();
    }
}
