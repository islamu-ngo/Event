using Explore.Domain;
using Explore.Persistence.Caching;
using Explore.Persistence.Database.ProviderPrimitives;
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
    [Arguments("routine-acl")]
    [Arguments("body")]
    [Arguments("policy")]
    [Arguments("tenant-policy")]
    [Arguments("extra-policy")]
    [Arguments("source-column")]
    [Arguments("source-type")]
    [Arguments("rls")]
    [Arguments("role")]
    [Arguments("owner-table-grant")]
    [Arguments("owner-secret-grant")]
    [Arguments("namespace-owner")]
    public async Task AdmissionAndReinstallationRefuseCatalogueDriftWithoutRepair(string change)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string function = PostgresGlobalLookupCatalogueContract.FunctionSql(writer);
        string roleTable = names.Table(typeof(Role));
        string types = names.Table(typeof(EventType));
        await writer.Database.ExecuteSqlRawAsync(change switch
        {
            "routine-acl" => $"GRANT EXECUTE ON FUNCTION {function}(integer) TO PUBLIC",
            "body" => ReplaceBody(function, names.Quote(names.Schema)),
            "policy" => $"ALTER POLICY global_lookup_catalogue_read ON {types} USING (true)",
            "tenant-policy" => $"ALTER POLICY tenant_isolation ON {types} USING (true)",
            "extra-policy" => $"CREATE POLICY catalogue_test_extra ON {types} TO {PostgresGlobalLookupCatalogueContract.OwnerRole} USING (true)",
            "source-column" => $"ALTER TABLE {roleTable} ADD COLUMN catalogue_test_extra integer",
            "source-type" => $"ALTER TABLE {roleTable} ALTER COLUMN {names.Column(typeof(Role), nameof(Role.Description))} TYPE text",
            "rls" => $"ALTER TABLE {types} NO FORCE ROW LEVEL SECURITY",
            "role" => $"ALTER ROLE {PostgresGlobalLookupCatalogueContract.RuntimeRole} INHERIT",
            "owner-table-grant" => $"GRANT SELECT ON {roleTable} TO {PostgresGlobalLookupCatalogueContract.OwnerRole}",
            "owner-secret-grant" => $"GRANT SELECT ON {names.Table(typeof(RolePermission))} TO {PostgresGlobalLookupCatalogueContract.OwnerRole}",
            "namespace-owner" => $"ALTER SCHEMA {names.Quote(names.Schema)} OWNER TO {PostgresGlobalLookupCatalogueContract.OwnerRole}",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        await transaction.CreateSavepointAsync("before_admission");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresGlobalLookupCatalogueContract.RequireReadyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_admission");
        denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresGlobalLookupCatalogueContract.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("body")]
    [Arguments("schema")]
    [Arguments("policy")]
    public async Task ActualRuntimeRefusesCommittedDriftAndCacheDoesNotInventEmptySuccess(string change)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        var names = new HumanPrincipalSourceSql(writer);
        string function = PostgresGlobalLookupCatalogueContract.FunctionSql(writer);
        string types = names.Table(typeof(EventType));
        string roleTable = names.Table(typeof(Role));
        string originalBody = await writer.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"",
            function + "(integer)").SingleAsync();
        string originalPolicy = await writer.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.pg_get_expr(p.polqual,p.polrelid) AS "Value" FROM pg_catalog.pg_policy p
            WHERE p.polrelid = pg_catalog.to_regclass({0}) AND p.polname = 'global_lookup_catalogue_read'
            """, types).SingleAsync();
        string policyStamp = await writer.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.obj_description(p.oid,'pg_policy') AS "Value" FROM pg_catalog.pg_policy p
            WHERE p.polrelid = pg_catalog.to_regclass({0}) AND p.polname = 'global_lookup_catalogue_read'
            """, types).SingleAsync();
        // Commit only the owned mutation so a genuinely separate Runtime connection
        // observes it. Finally restores the exact original capability, even on failure.
        try
        {
            await using (var mutation = await writer.Database.BeginTransactionAsync())
            {
                await writer.Database.ExecuteSqlRawAsync(change switch
                {
                    "body" => ReplaceBody(function, names.Quote(names.Schema)),
                    "schema" => $"ALTER TABLE {roleTable} ADD COLUMN catalogue_test_extra integer",
                    "policy" => $"ALTER POLICY global_lookup_catalogue_read ON {types} USING (true)",
                    _ => throw new ArgumentOutOfRangeException(nameof(change))
                });
                await mutation.CommitAsync();
            }
            await using var reader = Open(PrimaryDatabaseRole.Runtime);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                    reader, fixture.NativeQueueEnrollment, default));
            await Assert.That(denied!.SqlState).IsEqualTo("55000");

            var services = new ServiceCollection();
            services.AddSingleton(fixture.NativeQueueEnrollment);
            services.AddScoped(_ => Open(PrimaryDatabaseRole.Runtime));
            await using var provider = services.BuildServiceProvider();
            var cache = new LookupDataCache(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LookupDataCache>.Instance);
            await cache.InitializeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                _ = cache.Get<Role>();
                return Task.CompletedTask;
            });
        }
        finally
        {
            await using var restoration = await writer.Database.BeginTransactionAsync();
            if (change == "body")
            {
                await writer.Database.ExecuteSqlRawAsync(ReplaceDefinition(
                    names.Quote(names.Schema), originalBody));
            }
            else if (change == "schema")
                await writer.Database.ExecuteSqlRawAsync($"ALTER TABLE {roleTable} DROP COLUMN catalogue_test_extra");
            else
            {
                await writer.Database.ExecuteSqlRawAsync(
                    $"ALTER POLICY global_lookup_catalogue_read ON {types} USING ({originalPolicy})");
                // The predicate is identical; stamp its freshly parsed expression using
                // the original source fingerprint rather than assuming parser locations.
                string prefix = policyStamp[..(policyStamp.LastIndexOf(':') + 1)];
                string restoredStamp = await writer.Database.SqlQueryRaw<string>(
                    """
                    SELECT {0} || pg_catalog.md5(p.polqual::text) AS "Value" FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({1}) AND p.polname = 'global_lookup_catalogue_read'
                    """, prefix, types).SingleAsync();
                await writer.Database.ExecuteSqlRawAsync(
                    $"COMMENT ON POLICY global_lookup_catalogue_read ON {types} IS '{restoredStamp.Replace("'", "''", StringComparison.Ordinal)}'");
            }
            await restoration.CommitAsync();
        }
        await using var restored = Open(PrimaryDatabaseRole.Runtime);
        await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            restored, fixture.NativeQueueEnrollment, default)).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task UnexpectedRuntimeSourceGrantIsRefusedRatherThanUsed()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        var names = new HumanPrincipalSourceSql(writer);
        string target = names.Table(typeof(Role));
        string login = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        try
        {
            await writer.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {target} TO {login}");
            await using var reader = Open(PrimaryDatabaseRole.Runtime);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                    reader, fixture.NativeQueueEnrollment, default));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await writer.Database.ExecuteSqlRawAsync($"REVOKE SELECT ON {target} FROM {login}");
        }
    }

    private static string ReplaceBody(string function, string schema) => ReplaceDefinition(schema, $"""
        CREATE OR REPLACE FUNCTION {function}(p_type integer)
            RETURNS TABLE ("Id" integer,"MasterCode" text,"FullName" text,"Description" text,
                "MinAge" integer,"MaxAge" integer,"RoleScopeId" integer,"IsSystem" boolean,"TenantId" uuid)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $changed$ BEGIN RETURN; END; $changed$;
        """);

    private static string ReplaceDefinition(string schema, string definition) => $"""
        GRANT CREATE ON SCHEMA {schema} TO {PostgresGlobalLookupCatalogueContract.OwnerRole};
        DO $replace$
        DECLARE previous_role text := pg_catalog.current_setting('role');
        BEGIN
            PERFORM pg_catalog.set_config('role','{PostgresGlobalLookupCatalogueContract.OwnerRole}',true);
            EXECUTE $definition${definition}$definition$;
            PERFORM pg_catalog.set_config('role',previous_role,true);
        END $replace$;
        REVOKE CREATE ON SCHEMA {schema} FROM {PostgresGlobalLookupCatalogueContract.OwnerRole};
        """;
}
