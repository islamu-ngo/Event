using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
public sealed class GlobalLookupManagedWorkerCompositionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeLookupRemainsAdmittedAfterManagedWorkerEnrollment()
    {
        int[] expected;
        await using (var original = Open(PrimaryDatabaseRole.Runtime))
            expected = (await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                original, fixture.NativeQueueEnrollment, default)).Select(role => role.Id).Order().ToArray();

        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await PostgresTenantCreationPermissions.ApplyAsync(
            migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await using var global = await new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await using (var creatorReader = Open(PrimaryDatabaseRole.Runtime))
        {
            var creatorRoles = await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                creatorReader, fixture.NativeQueueEnrollment, default);
            await Assert.That(creatorRoles.Select(role => role.Id).Order().SequenceEqual(expected)).IsTrue();
        }
        await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
            migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);

        await RequireGlobalAsync(global);
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        var roles = await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            reader, fixture.NativeQueueEnrollment, default);
        await Assert.That(roles.Select(role => role.Id).Order().SequenceEqual(expected)).IsTrue();
    }

    private async Task RequireGlobalAsync(ExploreDbContext global)
    {
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options);
    }
}

[NotInParallel]
[ClassDataSource<ManagedWorkerPostgreSqlFixture>(Shared = SharedType.PerClass)]
public sealed class GlobalLookupManagedWorkerTamperTests(ManagedWorkerPostgreSqlFixture fixture)
{
    [Test]
    [Arguments("missing-email-verified")]
    [Arguments("missing-concurrency-stamp")]
    [Arguments("missing-creator-id")]
    [Arguments("table")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("grant-option")]
    [Arguments("outside-union")]
    public Task ComposedCatalogueRejectsTamperingAndRecoversOnOriginalOwner(string attack) =>
        AssertTamperAsync(attack);

    [Test]
    public Task UnrelatedWorkerSourceDriftInvalidatesUserFenceAndRecoversOnOriginalOwner() =>
        AssertTamperAsync("unrelated-worker-source");

    private async Task AssertTamperAsync(string attack)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await migrator.Database.OpenConnectionAsync();

        await using var runtime = Open(PrimaryDatabaseRole.Runtime);
        await runtime.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)runtime.Database.GetDbConnection();
        int backend = connection.ProcessID;
        var owner = runtime.ContextId;
        int[] expected = (await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            runtime, fixture.NativeQueueEnrollment, default)).Select(role => role.Id).Order().ToArray();
        await using var global = await new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireGlobalAsync(global);

        var names = new HumanPrincipalSourceSql(migrator);
        string user = names.Table(typeof(User));
        string role = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string workerOperation = names.Table(typeof(ManagedTenantProvisioningOperation));
        string workerRequestHash = names.Column(typeof(ManagedTenantProvisioningOperation), nameof(ManagedTenantProvisioningOperation.RequestHash));
        string inherited = names.Quote("worker_composition_attack_" + Guid.CreateVersion7().ToString("N"));
        string Column(string property) => names.Column(typeof(User), property);
        string id = Column(nameof(User.Id));
        string union = string.Join(", ", new[]
        {
            nameof(User.Id), nameof(User.LastActiveTenantId), nameof(User.EmailVerified),
            nameof(User.ConcurrencyStamp), nameof(User.UpdatedAt), nameof(User.UpdatedBy)
        }.Select(Column));
        var (apply, restore) = attack switch
        {
            "missing-email-verified" => Missing(nameof(User.EmailVerified)),
            "missing-concurrency-stamp" => Missing(nameof(User.ConcurrencyStamp)),
            "missing-creator-id" => Missing(nameof(User.Id)),
            "table" => ($"GRANT UPDATE ON {user} TO {role}",
                $"REVOKE UPDATE ON {user} FROM {role}; GRANT UPDATE ({union}) ON {user} TO {role}"),
            "public" => ($"GRANT UPDATE ({id}) ON {user} TO PUBLIC",
                $"REVOKE UPDATE ({id}) ON {user} FROM PUBLIC"),
            "inherited" => ($"CREATE ROLE {inherited} NOLOGIN; GRANT UPDATE ({id}) ON {user} TO {inherited}; "
                    + $"GRANT {inherited} TO {role} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {inherited} FROM {role}; REVOKE UPDATE ({id}) ON {user} FROM {inherited}; DROP ROLE {inherited}"),
            "grant-option" => ($"GRANT UPDATE ({id}) ON {user} TO {role} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR UPDATE ({id}) ON {user} FROM {role}"),
            "outside-union" => ($"GRANT UPDATE ({Column(nameof(User.CreatedAt))}) ON {user} TO {role}",
                $"REVOKE UPDATE ({Column(nameof(User.CreatedAt))}) ON {user} FROM {role}"),
            "unrelated-worker-source" => ($"REVOKE SELECT ({workerRequestHash}) ON {workerOperation} FROM {role}",
                $"GRANT SELECT ({workerRequestHash}) ON {workerOperation} TO {role}"),
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };
        string original = await CatalogueFingerprintAsync(migrator);
        try
        {
            await ExecuteAsync(migrator, apply);
            string tampered = await CatalogueFingerprintAsync(migrator);
            await Assert.That(tampered == original).IsFalse();
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                    runtime, fixture.NativeQueueEnrollment, default));
            await Assert.That(denied?.Message).IsEqualTo("global_lookup_read_permissions_not_ready");
            var creatorDenied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantCreationPermissions.RequireCurrentAsync(
                    global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(creatorDenied?.Message).IsEqualTo("tenant_creation_permissions_not_ready");
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantCreationPermissions.ApplyAsync(
                    migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(refusal?.Message).IsEqualTo("tenant_creation_permissions_drift");
            await Assert.That(await CatalogueFingerprintAsync(migrator)).IsEqualTo(tampered);
            var workerRefusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                    migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(workerRefusal?.Message).IsEqualTo("managed_tenant_worker_creation_permissions_drift");
            await Assert.That(await CatalogueFingerprintAsync(migrator)).IsEqualTo(tampered);
        }
        finally
        {
            await ExecuteAsync(migrator, restore);
        }
        await Assert.That(await CatalogueFingerprintAsync(migrator)).IsEqualTo(original);
        var recovered = await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            runtime, fixture.NativeQueueEnrollment, default);
        await Assert.That(recovered.Select(value => value.Id).Order().SequenceEqual(expected)).IsTrue();
        await Assert.That(runtime.ContextId).IsEqualTo(owner);
        await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
        await RequireGlobalAsync(global);

        (string Apply, string Restore) Missing(string property) =>
            ($"REVOKE UPDATE ({Column(property)}) ON {user} FROM {role}",
             $"GRANT UPDATE ({Column(property)}) ON {user} TO {role}");
    }

    private async Task RequireGlobalAsync(ExploreDbContext global)
    {
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
            global, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
    }

    private static async Task<string> CatalogueFingerprintAsync(ExploreDbContext migrator)
    {
        // Normalize ACL entry order; retain grantor, grantee, privilege and grant option.
        // Policy OIDs change on exact recreation, so compare their semantic catalogue image.
        await using var command = Command(migrator, """
            SELECT pg_catalog.jsonb_build_object(
                'acl', (SELECT pg_catalog.jsonb_agg(image ORDER BY image::text)
                    FROM (
                        SELECT pg_catalog.jsonb_build_array(n.nspname,c.relname,0,
                            acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable) image
                        FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                        CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                        WHERE n.nspname NOT IN ('pg_catalog','information_schema')
                        UNION ALL
                        SELECT pg_catalog.jsonb_build_array(n.nspname,c.relname,a.attnum,
                            acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable)
                        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
                        JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE n.nspname NOT IN ('pg_catalog','information_schema')
                    ) entries),
                'policy', (SELECT pg_catalog.jsonb_agg(image ORDER BY image::text)
                    FROM (SELECT pg_catalog.jsonb_build_array(polrelid,polname,polcmd,polpermissive,
                        polroles,polqual::text,polwithcheck::text,
                        pg_catalog.obj_description(oid,'pg_policy')) image FROM pg_catalog.pg_policy) policies),
                'membership', (SELECT pg_catalog.jsonb_agg(image ORDER BY image::text)
                    FROM (SELECT pg_catalog.to_jsonb(m)-'oid' image FROM pg_catalog.pg_auth_members m) members)
            )::text
            """);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Catalogue fingerprint is missing."));
    }

    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options);
    }
}
