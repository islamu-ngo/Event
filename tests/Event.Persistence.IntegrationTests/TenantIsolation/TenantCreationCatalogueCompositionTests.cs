using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
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
/// Exercises optional creator catalogue composition through existing native consumers,
/// not a fabricated human creation authority or a replacement credential.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class TenantCreationCatalogueCompositionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExistingConsumersAcceptAbsentOrCompleteCreationAuthorityButRejectEveryPartialOrWidenedState()
    {
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync();
        string before = await GraphDigestAsync(migrator);
        var names = new HumanPrincipalSourceSql(migrator);
        string globalLogin = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtimeLogin = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string tenant = names.Table(typeof(Tenant));
        string slug = names.Column(typeof(Tenant), nameof(Tenant.Slug));
        string privateName = names.Column(typeof(Tenant), nameof(Tenant.FullName));
        string membership = names.Table(typeof(TenantUser));
        string membershipActor = names.Column(typeof(TenantUser), nameof(TenantUser.CreatedBy));
        string user = names.Table(typeof(User));
        string privateUserColumn = names.Column(typeof(User), nameof(User.EmailVerified));
        string grant = names.Table(typeof(TenantUserRoleGrant));
        string revocation = names.Column(typeof(TenantUserRoleGrant), nameof(TenantUserRoleGrant.RevocationReason));

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        Guid selectedTenant = Guid.CreateVersion7();
        Guid absentPrincipal = Guid.CreateVersion7();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(selectedTenant));
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        var globalConnector = (NpgsqlConnection)global.Database.GetDbConnection();
        var runtimeConnector = (NpgsqlConnection)runtime.Database.GetDbConnection();
        int globalBackend = globalConnector.ProcessID;
        int runtimeBackend = runtimeConnector.ProcessID;
        var globalLease = global.ContextId;
        var runtimeLease = runtime.ContextId;

        // The class-local fixture has the existing consumers and discovery metadata,
        // but has not installed the optional creator package.
        await RequireConsumersAsync(allowed: true);
        await AttackAsync($"GRANT SELECT ({slug}) ON {tenant} TO {globalLogin}",
            $"REVOKE SELECT ({slug}) ON {tenant} FROM {globalLogin}");
        await AttackAsync($"GRANT SELECT ({privateUserColumn}) ON {user} TO {globalLogin}",
            $"REVOKE SELECT ({privateUserColumn}) ON {user} FROM {globalLogin}", publisherOnly: true);

        await PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment,
            IdentityDatabaseTopology.Colocated);
        await RequireConsumersAsync(allowed: true);

        await AttackAsync($"GRANT SELECT ({privateName}) ON {tenant} TO {globalLogin}",
            $"REVOKE SELECT ({privateName}) ON {tenant} FROM {globalLogin}");
        await AttackAsync($"REVOKE INSERT ({revocation}) ON {grant} FROM {globalLogin}",
            $"GRANT INSERT ({revocation}) ON {grant} TO {globalLogin}");
        await AttackAsync($"GRANT INSERT ({membershipActor}) ON {membership} TO {runtimeLogin}",
            $"REVOKE INSERT ({membershipActor}) ON {membership} FROM {runtimeLogin}");

        await Assert.That(global.ContextId).IsEqualTo(globalLease);
        await Assert.That(runtime.ContextId).IsEqualTo(runtimeLease);
        await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnector);
        await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(runtimeConnector);
        await Assert.That(globalConnector.ProcessID).IsEqualTo(globalBackend);
        await Assert.That(runtimeConnector.ProcessID).IsEqualTo(runtimeBackend);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(runtime.TenantFilterTenantId).IsEqualTo(selectedTenant);
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(runtime.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);

        async Task AttackAsync(string mutation, string inverse, bool publisherOnly = false)
        {
            // Each catalogue mutation is committed so both genuine application sessions
            // see it. Cleanup reverses only this test's exact grant/revocation, never repairs
            // an incomplete production manifest through ApplyAsync.
            await ExecuteAsync(migrator, mutation);
            try { await RequireConsumersAsync(allowed: false, publisherOnly: publisherOnly); }
            finally { await ExecuteAsync(migrator, inverse); }
            await RequireConsumersAsync(allowed: true);
            await Assert.That(await GraphDigestAsync(migrator)).IsEqualTo(before);
        }

        async Task RequireConsumersAsync(bool allowed, bool publisherOnly = false)
        {
            if (!publisherOnly)
            {
                await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    if (allowed)
                        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global,
                            fixture.NativeQueueEnrollment, globalOnly: true, CancellationToken.None);
                    else
                    {
                        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                            PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(global,
                                fixture.NativeQueueEnrollment, globalOnly: true, CancellationToken.None));
                        await Assert.That(denied.Message).IsEqualTo("discovery_metadata_current_owner_not_ready");
                    }
                    await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
            await using (var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                try
                {
                    if (allowed)
                    {
                        var result = await PostgresEventCreationPublisherReadContract.ReadAsync(runtime,
                            fixture.NativeQueueEnrollment, selectedTenant, absentPrincipal, CancellationToken.None);
                        await Assert.That(result.Organizations.Count + result.Groups.Count + result.Permissions.Count).IsEqualTo(0);
                    }
                    else
                    {
                        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                        {
                            await PostgresEventCreationPublisherReadContract.ReadAsync(runtime,
                                fixture.NativeQueueEnrollment, selectedTenant, absentPrincipal, CancellationToken.None);
                        });
                        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                        await Assert.That(denied.MessageText).IsEqualTo("event_creation_publisher_private_source_privilege");
                    }
                    await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
            if (!publisherOnly)
            {
                await using var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    if (allowed)
                        await PostgresManagedProviderProvisioningContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
                    else
                    {
                        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                            PostgresManagedProviderProvisioningContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
                        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
                        await Assert.That(denied.MessageText).IsEqualTo("human_provisioning_ambient_write_authority");
                    }
                    await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                }
                finally { await transaction.RollbackAsync(CancellationToken.None); }
            }
        }
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task<string> GraphDigestAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        string[] images = new[] { typeof(Tenant), typeof(TenantSettingsDocument), typeof(EventDiscoveryRevision),
                typeof(TenantUser), typeof(TenantUserRoleGrant), typeof(Role) }
            .Select(type => $"(SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY r.{names.Column(type, "Id")})::text,'[]')) FROM {names.Table(type)} r)")
            .ToArray();
        await using var command = Command(context, "SELECT " + string.Join(" || ':' || ", images));
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The independent graph observation is missing."));
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private static async Task ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
