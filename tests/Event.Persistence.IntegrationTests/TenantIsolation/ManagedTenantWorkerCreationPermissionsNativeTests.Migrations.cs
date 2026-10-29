using Explore.Application.Configuration;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class ManagedTenantWorkerCreationPermissionsNativeTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ProductionMigrationActivatesWorkerAndPolicyTogetherWithoutRepairingDrift(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = new WorkerFixture(topology);
        await fixture.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var migratorOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Provider"] = AuthenticationProviderDeploymentOptions.KeycloakProvider,
            ["IdentityDatabase:Topology"] = topology == IdentityDatabaseTopology.Colocated ? "colocated" : "external",
            ["PrivacyErasure:Authority:Topology"] = "CoLocated"
        };
        foreach (PrimaryDatabaseRole role in Enum.GetValues<PrimaryDatabaseRole>()
                     .Where(role => role != PrimaryDatabaseRole.LocalIdentityWriter))
            values[$"Database:{role}:Username"] = fixture.GetNativeQueueOptions(role).Username;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        Task MigrateAsync() => ExploreDatabaseMigrator.MigrateAndSeedAsync(
            environment, configuration, migratorOptions, NullLogger.Instance,
            seedDevelopmentData: false, cancellationToken: token);

        await using var observer = OpenMigrator(fixture);
        await observer.Database.OpenConnectionAsync(token);
        await RequireSessionAsync(observer, fixture.NativeQueueEnrollment.MigratorLogin);
        string runtimeBefore = await RuntimeGrantsAsync(observer, fixture.NativeQueueEnrollment.RuntimeLogin);
        await RequirePrivateSourcesUnreadableAsync(observer, fixture.NativeQueueEnrollment.GlobalLogin, token);

        // Only the real production migration entry point installs these two profiles.
        // No test-owned Apply, generated-table shortcut or permission grant participates.
        await MigrateAsync();
        var globalFactory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await globalFactory.CreateDbContextAsync(token);
        await global.Database.OpenConnectionAsync(token);
        await RequireSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
        var originalConnection = (NpgsqlConnection)global.Database.GetDbConnection();
        int originalBackend = originalConnection.ProcessID;
        var originalContext = global.ContextId;

        async Task RequireBothProfilesAsync()
        {
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
                global, fixture.NativeQueueEnrollment, topology, token);
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, token);
            await Assert.That(global.ContextId).IsEqualTo(originalContext);
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(originalConnection);
            await Assert.That(originalConnection.ProcessID).IsEqualTo(originalBackend);
            await Assert.That(global.Database.CurrentTransaction).IsNull();
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.IsTenantFilterBypassed).IsFalse();
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        }

        await RequireBothProfilesAsync();
        string rowCounts = await RowCountsAsync(observer);
        string profileImage = await MigrationProfileImageAsync(observer, token);
        await Assert.That(await RuntimeGrantsAsync(observer, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);
        await RequirePrivateSourcesUnreadableAsync(observer, fixture.NativeQueueEnrollment.GlobalLogin, token);

        await MigrateAsync();
        await RequireBothProfilesAsync();
        await Assert.That(await RowCountsAsync(observer)).IsEqualTo(rowCounts);
        await Assert.That(await MigrationProfileImageAsync(observer, token)).IsEqualTo(profileImage);
        await Assert.That(await RuntimeGrantsAsync(observer, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);
        await RequirePrivateSourcesUnreadableAsync(observer, fixture.NativeQueueEnrollment.GlobalLogin, token);

        var entity = observer.Model.FindEntityType(typeof(ManagedTenantProvisioningOperation))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = observer.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(table.Name, table.Schema);
        string column = names.DelimitIdentifier(entity.FindProperty(nameof(ManagedTenantProvisioningOperation.FailureCode))!
            .GetColumnName(table)!);
        string login = names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        await using (var revoke = Command(observer, $"REVOKE UPDATE ({column}) ON {relation} FROM {login}"))
            await revoke.ExecuteNonQueryAsync(token);
        try
        {
            string revoked = await MigrationProfileImageAsync(observer, token);
            var drift = await Assert.ThrowsAsync<InvalidOperationException>(MigrateAsync);
            await Assert.That(drift.Message).IsEqualTo("managed_tenant_worker_creation_permissions_drift");
            await Assert.That(await MigrationProfileImageAsync(observer, token)).IsEqualTo(revoked);
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
                    global, fixture.NativeQueueEnrollment, topology, token));
            await Assert.That(denied.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready");
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, token);
            await Assert.That(await RowCountsAsync(observer)).IsEqualTo(rowCounts);
            await Assert.That(await RuntimeGrantsAsync(observer, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);
            await RequirePrivateSourcesUnreadableAsync(observer, fixture.NativeQueueEnrollment.GlobalLogin, token);
        }
        finally
        {
            // Undo only this invocation's single revocation; never reinstall either profile.
            await using var restore = Command(observer, $"GRANT UPDATE ({column}) ON {relation} TO {login}");
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await RequireBothProfilesAsync();
        await Assert.That(await MigrationProfileImageAsync(observer, token)).IsEqualTo(profileImage);
    }

    private static async Task RequirePrivateSourcesUnreadableAsync(
        ExploreDbContext observer, string login, CancellationToken token)
    {
        var names = new HumanPrincipalSourceSql(observer);
        await using var command = Command(observer, """
            SELECT NOT EXISTS (
                SELECT 1 FROM unnest(@tables::text[]) source(relation)
                CROSS JOIN pg_catalog.pg_roles member
                WHERE pg_catalog.pg_has_role(@login,member.oid,'MEMBER')
                    AND (pg_catalog.has_table_privilege(member.oid,source.relation,'SELECT')
                        OR pg_catalog.has_any_column_privilege(member.oid,source.relation,'SELECT')))
            """);
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("tables", new[]
        {
            names.Table(typeof(UserPii)), names.Table(typeof(ActorPii)), names.Table(typeof(SystemSetting))
        });
        await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
    }

    private static async Task<string> MigrationProfileImageAsync(ExploreDbContext context, CancellationToken token)
    {
        // Other native installers can recreate equivalent policies with new catalog OIDs.
        // Compare logical ACLs and these two profiles' policy definitions, not allocation identities.
        await using var command = Command(context, """
            WITH tables AS (SELECT pg_catalog.to_regclass(name) AS id FROM unnest(@tables::text[]) name),
            grants AS (
                SELECT c.oid AS relation,0::smallint AS attribute,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_class c JOIN tables t ON t.id=c.oid
                CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                UNION ALL
                SELECT a.attrelid,a.attnum,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_attribute a JOIN tables t ON t.id=a.attrelid
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl),
            policies AS (
                SELECT p.polrelid,p.polname,p.polcmd,p.polpermissive,p.polroles,
                    pg_catalog.pg_get_expr(p.polqual,p.polrelid) AS predicate,
                    pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) AS check_predicate,
                    pg_catalog.obj_description(p.oid,'pg_policy') AS fingerprint
                FROM pg_catalog.pg_policy p JOIN tables t ON t.id=p.polrelid
                WHERE starts_with(p.polname,'managed_tenant_worker_')
                    OR starts_with(p.polname,'provisioning_policy_'))
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_object(
                'grants',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(g)
                    ORDER BY g.relation,g.attribute,g.grantor,g.grantee,g.privilege_type,g.is_grantable) FROM grants g),
                'policies',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(p)
                    ORDER BY p.polrelid,p.polname) FROM policies p))::text)
            """);
        command.Parameters.AddWithValue("tables", Tables(context));
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new InvalidOperationException("The production provisioning profile image is absent."));
    }
}
