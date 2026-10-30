using System.Data;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class DiscoveryMetadataAuthorityTests
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task QuotedEnrollmentRoleRenameRejectsWithoutParsingOrChangingAuthority(PrimaryDatabaseRole renamedRole)
    {
        await using var global = await OpenGlobalAsync();
        await global.Database.OpenConnectionAsync();
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, true, default);

        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        await migrator.Database.OpenConnectionAsync();
        var migratorConnector = (NpgsqlConnection)migrator.Database.GetDbConnection();
        int migratorBackend = migratorConnector.ProcessID;
        var migratorLease = migrator.ContextId;
        await using (var identity = EnrollmentCommand(migrator,
            "SELECT current_user=session_user AND session_user=@login"))
        {
            identity.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.MigratorLogin));
            await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
        }

        string originalName = renamedRole == PrimaryDatabaseRole.Runtime
            ? fixture.NativeQueueEnrollment.RuntimeLogin : fixture.NativeQueueEnrollment.GlobalLogin;
        string renamed = "Meta$guard$'\\_" + Guid.CreateVersion7().ToString("N")[..12];
        var changedEnrollment = renamedRole == PrimaryDatabaseRole.Runtime
            ? fixture.NativeQueueEnrollment with { RuntimeLogin = renamed }
            : fixture.NativeQueueEnrollment with { GlobalLogin = renamed };
        var names = new HumanPrincipalSourceSql(migrator);
        long roleOid;
        await using (var role = EnrollmentCommand(migrator,
            "SELECT oid::bigint FROM pg_catalog.pg_roles WHERE rolname=@login"))
        {
            role.Parameters.Add(new NpgsqlParameter("login", originalName));
            roleOid = (long)(await role.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("The configured role is missing."));
        }
        string originalImage = await EnrollmentMetadataImageAsync(migrator);
        Exception? rejection = null;
        await migrator.Database.ExecuteSqlRawAsync(
            $"ALTER ROLE {names.Quote(originalName)} RENAME TO {names.Quote(renamed)}");
        try
        {
            await Assert.That(await EnrollmentRoleNameAsync(migrator, roleOid)).IsEqualTo(renamed);
            await using (var identity = EnrollmentCommand(global,
                "SELECT current_user=session_user AND session_user=@login"))
            {
                identity.Parameters.Add(new NpgsqlParameter("login", changedEnrollment.GlobalLogin));
                await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
            }
            await Assert.That(await EnrollmentMetadataImageAsync(migrator)).IsEqualTo(originalImage);
            try
            {
                // The policies still contain the original enrollment. A renamed login is
                // not a replacement authority, but its name must remain data, not SQL syntax.
                await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, changedEnrollment, true, default);
            }
            catch (Exception exception)
            {
                rejection = exception;
            }
            await Assert.That(await EnrollmentMetadataImageAsync(migrator)).IsEqualTo(originalImage);
            await AssertOriginalOwnersAsync();
        }
        finally
        {
            // Restore only this invocation's exact role rename. No policies, ACLs,
            // routine bodies, or installed capabilities are changed or reinstalled.
            await migrator.Database.ExecuteSqlRawAsync(
                $"ALTER ROLE {names.Quote(renamed)} RENAME TO {names.Quote(originalName)}");
        }

        await Assert.That(await EnrollmentRoleNameAsync(migrator, roleOid)).IsEqualTo(originalName);
        await Assert.That(await EnrollmentMetadataImageAsync(migrator)).IsEqualTo(originalImage);
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, true, default);
        await AssertOriginalOwnersAsync();
        // Check the rejection after restoration controls, including when the current
        // implementation throws a PostgreSQL parsing error or unexpectedly admits it.
        await Assert.That(rejection).IsNotNull();
        await Assert.That(rejection!.GetType()).IsEqualTo(typeof(UnauthorizedAccessException));
        await Assert.That(rejection.Message).IsEqualTo("discovery_metadata_current_owner_not_ready");

        async Task AssertOriginalOwnersAsync()
        {
            await Assert.That(global.ContextId).IsEqualTo(lease);
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(global.Database.CurrentTransaction).IsNull();
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.IsTenantFilterBypassed).IsFalse();
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            await Assert.That(migrator.ContextId).IsEqualTo(migratorLease);
            await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(migratorConnector);
            await Assert.That(migratorConnector.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(migratorConnector.ProcessID).IsEqualTo(migratorBackend);
            await Assert.That(migrator.Database.CurrentTransaction).IsNull();
        }
    }

    private static async Task<string> EnrollmentMetadataImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = EnrollmentCommand(context, """
            WITH relations AS (
                SELECT c.oid, c.relrowsecurity, c.relforcerowsecurity, c.relacl
                FROM pg_catalog.pg_class c
                WHERE c.oid IN (pg_catalog.to_regclass(@tenant),pg_catalog.to_regclass(@revision)))
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_object(
                'relations', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(oid,relrowsecurity,relforcerowsecurity) ORDER BY oid) FROM relations),
                'table_grants', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(r.oid,a.grantor,a.grantee,a.privilege_type,a.is_grantable)
                    ORDER BY r.oid,a.grantor,a.grantee,a.privilege_type,a.is_grantable)
                    FROM relations r CROSS JOIN LATERAL pg_catalog.aclexplode(r.relacl) a),
                'column_grants', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(c.attrelid,c.attnum,a.grantor,a.grantee,a.privilege_type,a.is_grantable)
                    ORDER BY c.attrelid,c.attnum,a.grantor,a.grantee,a.privilege_type,a.is_grantable)
                    FROM pg_catalog.pg_attribute c JOIN relations r ON r.oid=c.attrelid
                    CROSS JOIN LATERAL pg_catalog.aclexplode(c.attacl) a),
                'policies', (SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(p)
                    || pg_catalog.jsonb_build_object('comment',pg_catalog.obj_description(p.oid,'pg_policy')) ORDER BY p.oid)
                    FROM pg_catalog.pg_policy p JOIN relations r ON r.oid=p.polrelid))::text)
            """);
        command.Parameters.Add(new NpgsqlParameter("tenant", names.Table(typeof(Tenant))));
        command.Parameters.Add(new NpgsqlParameter("revision", names.Table(typeof(EventDiscoveryRevision))));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The original metadata authority image is missing."));
    }

    private static async Task<string> EnrollmentRoleNameAsync(ExploreDbContext context, long oid)
    {
        await using var command = EnrollmentCommand(context,
            "SELECT rolname::text FROM pg_catalog.pg_roles WHERE oid::bigint=@oid");
        command.Parameters.Add(new NpgsqlParameter("oid", oid));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The original role identity is missing."));
    }

    private static NpgsqlCommand EnrollmentCommand(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());
}
