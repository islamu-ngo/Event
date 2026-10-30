using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantPreferenceContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task QuotedDifferentGlobalEnrollmentCannotReplaceTheActualCaller()
    {
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
            var different = fixture.NativeQueueEnrollment with
            {
                GlobalLogin = "Other$guard$'\\" + Guid.CreateVersion7().ToString("N")[..12]
            };
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantPreferencePermissions.RequireReadyAsync(context, different));
            await Assert.That(denied.Message).IsEqualTo("tenant_preference_wrong_identity");
            await PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task ASerializableTransactionDoesNotSubstituteForTheConfiguredGlobal(PrimaryDatabaseRole role)
    {
        await using var context = Open(role);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.Message).IsEqualTo("tenant_preference_wrong_identity");
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task GlobalRequiresTheOriginalSerializableTransactionAndExplicitEnrollment()
    {
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
            var missing = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantPreferencePermissions.RequireReadyAsync(context, null));
            await Assert.That(missing.Message).IsEqualTo("tenant_preference_enrollment_required");
            await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments("global-private")]
    [Arguments("global-write")]
    [Arguments("runtime-write")]
    [Arguments("runtime-via-login")]
    [Arguments("grant-option")]
    [Arguments("system-column")]
    [Arguments("public")]
    [Arguments("admin-only")]
    [Arguments("partial")]
    [Arguments("rls")]
    public async Task EffectivePrivilegeDriftFailsOriginalEntryAndInstallerWithoutRepair(string attack)
    {
        await using var strategy = Open(PrimaryDatabaseRole.Migrator);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = Open(PrimaryDatabaseRole.Migrator);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var names = new HumanPrincipalSourceSql(context);
            string table = names.Table(typeof(User));
            string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            string preference = names.Quote(names.ColumnName(typeof(User), nameof(User.LastActiveTenantId)));
            string audit = names.Quote(names.ColumnName(typeof(User), nameof(User.UpdatedBy)));
            string other = names.Quote("preference_drift_" + Guid.CreateVersion7().ToString("N"));
            string useGlobal = $"SET LOCAL SESSION AUTHORIZATION {global}";
            await transaction.CreateSavepointAsync("healthy_identity");
            await context.Database.ExecuteSqlRawAsync(useGlobal);
            await PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
            await transaction.RollbackToSavepointAsync("healthy_identity");

            string change = attack switch
            {
                "global-private" => $"GRANT SELECT ({names.Quote(names.ColumnName(typeof(User), nameof(User.CreatedAt)))}) ON {table} TO {global}",
                "global-write" => $"GRANT UPDATE ({names.Quote(names.ColumnName(typeof(User), nameof(User.IsDeleted)))}) ON {table} TO {global}",
                "runtime-write" => $"GRANT UPDATE ({preference}) ON {table} TO {runtime}",
                "runtime-via-login" => $"GRANT {global} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "grant-option" => $"GRANT UPDATE ({preference}) ON {table} TO {global} WITH GRANT OPTION",
                "system-column" => $"GRANT SELECT (xmin) ON {table} TO {global}",
                "public" => $"GRANT SELECT ({preference}) ON {table} TO PUBLIC",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT UPDATE ({preference}) ON {table} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "partial" => $"REVOKE UPDATE ({audit}) ON {table} FROM {global}",
                "rls" => $"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY",
                _ => throw new ArgumentOutOfRangeException(nameof(attack))
            };
            await context.Database.ExecuteSqlRawAsync(change);
            string before = await CatalogueAsync(context);
            await transaction.CreateSavepointAsync("runtime_identity");
            await context.Database.ExecuteSqlRawAsync(useGlobal);
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantPreferencePermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.Message).IsEqualTo("tenant_preference_permissions_drift");
            await transaction.RollbackToSavepointAsync("runtime_identity");
            var install = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantPreferencePermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(install.Message).IsEqualTo("tenant_preference_permissions_drift");
            await Assert.That(await CatalogueAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReapplicationKeepsTheOriginalTransactionAndNoBusinessRoutinesOrCapabilityRoles()
    {
        await using var context = Open(PrimaryDatabaseRole.Migrator);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            string before = await CatalogueAsync(context);
            await PostgresTenantPreferencePermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresTenantPreferencePermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
            await Assert.That(await CatalogueAsync(context)).IsEqualTo(before);
            bool absent = await context.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc WHERE proname IN
                    ('human_tenant_preference_write','human_tenant_preference_image'))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                        ('event_tenant_preference_owner','event_tenant_preference_global','event_tenant_preference_migrator')) AS "Value"
                """).SingleAsync();
            await Assert.That(absent).IsTrue();
            await transaction.RollbackAsync();
        });
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task<string> CatalogueAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object('columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attacl::text) ORDER BY a.attnum)
                FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass(@table)),
                'table',(SELECT jsonb_build_array(c.relacl::text,c.relrowsecurity,c.relforcerowsecurity,c.relowner)
                    FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)),
                'memberships',(SELECT jsonb_agg(jsonb_build_array(roleid,member,grantor,admin_option,inherit_option,set_option) ORDER BY roleid,member,grantor)
                    FROM pg_catalog.pg_auth_members))::text
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = names.Table(typeof(User));
        command.Parameters.Add(parameter);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
