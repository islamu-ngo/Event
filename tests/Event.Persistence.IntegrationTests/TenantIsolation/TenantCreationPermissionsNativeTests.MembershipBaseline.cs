using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class TenantCreationPermissionsNativeTests
{
    [Test]
    public async Task FreshCreatorRecognizesExistingMembershipStatusUpdatesWithoutAdmittingOtherUpdates()
    {
        // A private fixture makes the first-install assertion independent of this
        // class's other installation tests. No previously installed grants are erased.
        await using var isolated = new PostgreSqlContainerFixture();
        await isolated.InitializeAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        var selected = isolated.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, selected);
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            new RelationalNamespaceOptionsExtension(selected.Schema, selected.Schema));
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        await migrator.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migrator);
        var sql = migrator.GetService<ISqlGenerationHelper>();
        string tenant = names.Table(typeof(Tenant));
        string id = names.ColumnName(typeof(Tenant), nameof(Tenant.Id));
        string status = names.ColumnName(typeof(Tenant), nameof(Tenant.TenantStatusId));
        await using (var original = Command(migrator, """
            SELECT pg_catalog.has_column_privilege(@login,@table,@status,'UPDATE')
                AND NOT pg_catalog.has_column_privilege(@login,@table,@id,'INSERT')
            """))
        {
            original.Parameters.AddWithValue("login", isolated.NativeQueueEnrollment.GlobalLogin);
            original.Parameters.AddWithValue("table", tenant);
            original.Parameters.AddWithValue("status", status);
            original.Parameters.AddWithValue("id", id);
            await Assert.That(await original.ExecuteScalarAsync()).IsEqualTo(true);
        }
        var factory = new GlobalExploreDbContextFactory(isolated.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(global, isolated.NativeQueueEnrollment);
        string originalRows = await GraphDigestAsync(migrator);
        await using (var installation = await migrator.Database.BeginTransactionAsync())
        {
            var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
            int backend = connection.ProcessID;
            var physical = installation.GetDbTransaction();
            await PostgresTenantCreationPermissions.ApplyAsync(migrator, isolated.NativeQueueEnrollment,
                IdentityDatabaseTopology.Colocated);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(installation);
            await Assert.That(installation.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await installation.CommitAsync();
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, isolated.NativeQueueEnrollment,
            IdentityDatabaseTopology.Colocated);
        string complete = await TenantAclImageAsync();
        await PostgresTenantCreationPermissions.ApplyAsync(migrator, isolated.NativeQueueEnrollment,
            IdentityDatabaseTopology.Colocated);
        await Assert.That(await TenantAclImageAsync()).IsEqualTo(complete);
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(originalRows);

        string role = sql.DelimitIdentifier(isolated.NativeQueueEnrollment.GlobalLogin);
        string slug = sql.DelimitIdentifier(names.ColumnName(typeof(Tenant), nameof(Tenant.Slug)));
        await ExecuteAsync(migrator, $"GRANT UPDATE ({slug}) ON {tenant} TO {role}");
        try
        {
            string attacked = await TenantAclImageAsync();
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantCreationPermissions.ApplyAsync(migrator, isolated.NativeQueueEnrollment,
                    IdentityDatabaseTopology.Colocated));
            await Assert.That(failure.Message).IsEqualTo("tenant_creation_permissions_drift");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantCreationPermissions.RequireCurrentAsync(global, isolated.NativeQueueEnrollment,
                    IdentityDatabaseTopology.Colocated));
            await Assert.That(await TenantAclImageAsync()).IsEqualTo(attacked);
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(originalRows);
        }
        finally
        {
            await ExecuteAsync(migrator, $"REVOKE UPDATE ({slug}) ON {tenant} FROM {role}");
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, isolated.NativeQueueEnrollment,
            IdentityDatabaseTopology.Colocated);
        await Assert.That(await TenantAclImageAsync()).IsEqualTo(complete);

        async Task<string> TenantAclImageAsync()
        {
            await using var command = Command(migrator, """
                SELECT jsonb_agg(jsonb_build_array(a.attnum,COALESCE(
                    (SELECT jsonb_agg(jsonb_build_array(acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                        ORDER BY acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                        FROM pg_catalog.aclexplode(a.attacl) acl),'[]'::jsonb)) ORDER BY a.attnum)::text
                FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0
                """);
            command.Parameters.AddWithValue("table", tenant);
            return (string)(await command.ExecuteScalarAsync())!;
        }
    }
}
