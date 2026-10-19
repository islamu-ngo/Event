using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Caching;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class GlobalLookupCatalogueNativeContractTests
{
    [Test]
    public async Task ModelExpansionCannotSilentlyWidenTheReferenceProjection()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await using var context = new ExpandedCatalogueContext(builder.Options);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(context, fixture.NativeQueueEnrollment, default));
        await Assert.That(denied!.Message).IsEqualTo("global_lookup_model_shape_drift");
    }

    [Test]
    [Arguments("table-select")]
    [Arguments("grant-option")]
    [Arguments("column-write")]
    [Arguments("extra-column")]
    [Arguments("system-column")]
    [Arguments("missing-column")]
    [Arguments("private-source")]
    [Arguments("private-system-column")]
    [Arguments("permission-source")]
    [Arguments("global-source")]
    [Arguments("admin-membership")]
    public async Task ReinstallationRefusesExpandedOrIncompletePermissionsWithoutRepair(string change)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string table = names.Table(typeof(Role));
        string login = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string description = names.Column(typeof(Role), nameof(Role.Description));
        string driftRole = names.Quote("catalogue_drift_" + Guid.CreateVersion7().ToString("N"));
        await writer.Database.ExecuteSqlRawAsync(change switch
        {
            "table-select" => $"GRANT SELECT ON {table} TO {login}",
            "grant-option" => $"GRANT SELECT ({description}) ON {table} TO {login} WITH GRANT OPTION",
            "column-write" => $"GRANT UPDATE ({description}) ON {table} TO {login}",
            "extra-column" => $"ALTER TABLE {table} ADD COLUMN catalogue_extra text; GRANT SELECT (catalogue_extra) ON {table} TO {login}",
            "system-column" => $"GRANT SELECT (xmin) ON {table} TO {login}",
            "missing-column" => $"REVOKE SELECT ({description}) ON {table} FROM {login}",
            "private-source" => $"GRANT SELECT ON {names.Table(typeof(UserPii))} TO {login}",
            "private-system-column" => $"GRANT SELECT (xmin) ON {names.Table(typeof(UserPii))} TO {login}",
            "permission-source" => $"GRANT SELECT ON {names.Table(typeof(RolePermission))} TO {login}",
            "global-source" => $"GRANT SELECT ({description}) ON {table} TO {names.Quote(fixture.NativeQueueEnrollment.GlobalLogin)}",
            "admin-membership" => $"CREATE ROLE {driftRole} NOLOGIN; GRANT SELECT ({description}) ON {table} TO {driftRole}; GRANT {driftRole} TO {login} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresLookupCatalogueReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global, false)]
    [Arguments(PrimaryDatabaseRole.Global, true)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, false)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, true)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, false)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, true)]
    public async Task CataloguePermissionsRejectCrossPurposeAccountMembership(PrimaryDatabaseRole purpose, bool runtimeReceives)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string other = names.Quote(purpose switch
        {
            PrimaryDatabaseRole.Global => fixture.NativeQueueEnrollment.GlobalLogin,
            PrimaryDatabaseRole.EmailPolicyInstanceMutation => fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            PrimaryDatabaseRole.EmailPolicyTenantMutation => fixture.NativeQueueEnrollment.EmailTenantMutationLogin,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        });
        await writer.Database.ExecuteSqlRawAsync(
            $"GRANT {(runtimeReceives ? other : runtime)} TO {(runtimeReceives ? runtime : other)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresLookupCatalogueReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await Assert.That(denied.MessageText).IsEqualTo("global_lookup_read_permissions_drift");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PostgresEmailDeliveryPolicyContract.InstanceMutationRole)]
    [Arguments(PostgresEmailDeliveryPolicyContract.TenantMutationRole)]
    public async Task CataloguePermissionsRejectRuntimeBorrowingIndependentEmailAuthority(string emailRole)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        await writer.Database.ExecuteSqlRawAsync(
            $"GRANT {names.Quote(emailRole)} TO {names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresLookupCatalogueReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await Assert.That(denied.MessageText).IsEqualTo("global_lookup_read_permissions_drift");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("table-select")]
    [Arguments("admin-membership")]
    public async Task ActualRuntimeRefusesCommittedPrivilegeExpansionAndCacheDoesNotInventEmptySuccess(string change)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        var names = new HumanPrincipalSourceSql(writer);
        string table = names.Table(typeof(Role));
        string login = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string driftRole = names.Quote("catalogue_drift_" + Guid.CreateVersion7().ToString("N"));
        string referenceColumns = string.Join(",", EfFirstProperties(typeof(Role))
            .Select(property => names.Column(typeof(Role), property)));
        await using (var baseline = Open(PrimaryDatabaseRole.Runtime))
        {
            await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                baseline, fixture.NativeQueueEnrollment, default)).Count).IsGreaterThan(0);
        }
        try
        {
            await writer.Database.ExecuteSqlRawAsync(change == "table-select"
                ? $"GRANT SELECT ON {table} TO {login}"
                : $"CREATE ROLE {driftRole} NOLOGIN; GRANT SELECT ON {table} TO {driftRole}; GRANT {driftRole} TO {login} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE");
            await using var reader = Open(PrimaryDatabaseRole.Runtime);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(reader, fixture.NativeQueueEnrollment, default));

            var services = new ServiceCollection();
            services.AddSingleton(fixture.NativeQueueEnrollment);
            services.AddSingleton<IDbContextFactory<ExploreDbContext>>(
                new EfFirstCatalogueContextFactory(() => Open(PrimaryDatabaseRole.Runtime)));
            await using var provider = services.BuildServiceProvider();
            using var cache = new LookupDataCache(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LookupDataCache>.Instance);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cache.InitializeAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                _ = cache.Get<Role>();
                return Task.CompletedTask;
            });
        }
        finally
        {
            await using var restoration = await writer.Database.BeginTransactionAsync();
            // PostgreSQL table REVOKE also removes column grants; restore only the declared baseline.
            await writer.Database.ExecuteSqlRawAsync(change == "table-select"
                ? $"REVOKE SELECT ON {table} FROM {login}; GRANT SELECT ({referenceColumns}) ON {table} TO {login}"
                : $"REVOKE {driftRole} FROM {login}; REVOKE SELECT ON {table} FROM {driftRole}; DROP ROLE {driftRole}");
            await restoration.CommitAsync();
        }
        await using var restored = Open(PrimaryDatabaseRole.Runtime);
        await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            restored, fixture.NativeQueueEnrollment, default)).Count).IsGreaterThan(0);
    }

    [Test]
    [Arguments("role")]
    [Arguments("routine")]
    public async Task ExistingCatalogueArtifactsRequireFreshDevelopmentDatabaseWithoutDestructiveCleanup(string artifact)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        await writer.Database.ExecuteSqlRawAsync(artifact == "role"
            ? "CREATE ROLE event_global_lookup_runtime NOLOGIN"
            : $"CREATE FUNCTION {names.Name("global_lookup_catalogue")}(integer) RETURNS integer LANGUAGE sql RETURN 0");
        await transaction.CreateSavepointAsync("before_installation");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresLookupCatalogueReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await Assert.That(denied.MessageText).IsEqualTo("global_lookup_requires_fresh_development_database");
        await transaction.RollbackToSavepointAsync("before_installation");
        bool remains = artifact == "role"
            ? await writer.Database.SqlQueryRaw<bool>(
                "SELECT pg_catalog.to_regrole('event_global_lookup_runtime') IS NOT NULL AS \"Value\"").SingleAsync()
            : await writer.Database.SqlQueryRaw<bool>(
                "SELECT pg_catalog.to_regprocedure({0}) IS NOT NULL AS \"Value\"",
                names.Name("global_lookup_catalogue") + "(integer)").SingleAsync();
        await Assert.That(remains).IsTrue();
        await transaction.RollbackAsync();
    }

    private sealed class ExpandedCatalogueContext(DbContextOptions<ExploreDbContext> options) : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Role>().Property<string>("CatalogueExtra");
        }
    }
}
