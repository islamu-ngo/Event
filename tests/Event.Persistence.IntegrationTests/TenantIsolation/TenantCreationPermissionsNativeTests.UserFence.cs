using System.Data;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class TenantCreationPermissionsNativeTests
{
    [Test]
    public async Task CreatorUserKeyFencePreservesPrivateColumnAndRuntimeWriteDenials()
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string user = names.Table(typeof(User));
        string id = names.Column(typeof(User), nameof(User.Id));
        string verified = names.Column(typeof(User), nameof(User.EmailVerified));
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var command = Command(global, $"UPDATE {user} SET {id}={id} WHERE FALSE");
            await Assert.That(await command.ExecuteNonQueryAsync()).IsEqualTo(0);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync();
        }
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var command = Command(global, $"UPDATE {user} SET {verified}={verified} WHERE FALSE");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(denied.SqlState).IsEqualTo("42501");
            await transaction.RollbackAsync();
        }

        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await using var runtime = new ExploreDbContext(options.Options);
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await using (var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var command = Command(runtime, $"UPDATE {user} SET {id}={id} WHERE FALSE");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(denied.SqlState).IsEqualTo("42501");
            await transaction.RollbackAsync();
        }
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("another-column")]
    [Arguments("table")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("grant-option")]
    public async Task CreatorUserFenceRejectsMissingOrExpandedAuthority(string attack)
    {
        await using var migrator = OpenMigrator();
        await InstallAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string user = names.Table(typeof(User));
        string id = names.Column(typeof(User), nameof(User.Id));
        string verified = names.Column(typeof(User), nameof(User.EmailVerified));
        string globalRole = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string inherited = names.Quote("creator_fence_attack_" + Guid.CreateVersion7().ToString("N"));
        var (apply, restore) = attack switch
        {
            "missing" => ($"REVOKE UPDATE ({id}) ON {user} FROM {globalRole}",
                $"GRANT UPDATE ({id}) ON {user} TO {globalRole}"),
            "another-column" => ($"GRANT UPDATE ({verified}) ON {user} TO {globalRole}",
                $"REVOKE UPDATE ({verified}) ON {user} FROM {globalRole}"),
            "table" => ($"GRANT UPDATE ON {user} TO {globalRole}",
                $"REVOKE UPDATE ON {user} FROM {globalRole}"),
            "public" => ($"GRANT UPDATE ({id}) ON {user} TO PUBLIC",
                $"REVOKE UPDATE ({id}) ON {user} FROM PUBLIC"),
            "inherited" => ($"CREATE ROLE {inherited} NOLOGIN; GRANT UPDATE ({id}) ON {user} TO {inherited}; "
                    + $"GRANT {inherited} TO {globalRole} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {inherited} FROM {globalRole}; REVOKE UPDATE ({id}) ON {user} FROM {inherited}; DROP ROLE {inherited}"),
            "grant-option" => ($"GRANT UPDATE ({id}) ON {user} TO {globalRole} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR UPDATE ({id}) ON {user} FROM {globalRole}"),
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };
        string before = await GraphDigestAsync(migrator);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalIdentityAsync(global);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await ExecuteAsync(migrator, apply);
        try
        {
            await AssertRejectedOnOriginalOwnerAsync(global, IdentityDatabaseTopology.Colocated);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantCreationPermissions.ApplyAsync(
                    migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(refusal.Message).IsEqualTo("tenant_creation_permissions_drift");
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
        }
        finally
        {
            await ExecuteAsync(migrator, restore);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
    }
}
