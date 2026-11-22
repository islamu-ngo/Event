using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class LocalAdministrationNativeContractTests
{
    [Test]
    [Arguments("body")]
    [Arguments("executor")]
    [Arguments("search_path")]
    public async Task CatalogueGuardRejectsStoredEntryDriftAndInstallerCannotRepairIt(string drift)
    {
        var source = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        string signature = Native.FunctionSql(migration, "assert_catalogue") + "()";
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        const string counterfeit = "BEGIN RETURN; END;";
        string change = drift switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {signature} RETURNS void
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                COMMENT ON FUNCTION {signature} IS
                    'local-administration:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """,
            "executor" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "search_path" => $"ALTER FUNCTION {signature} SET search_path = public, pg_catalog",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        try
        {
            await ExecuteCatalogueStatementAsync(migration, change);
            string changed = await HelperFingerprintAsync(migration, signature);
            await using var runtime = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
            await runtime.Database.OpenConnectionAsync();
            await using var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
                ReadStoredCatalogueAsync(runtime, source.Actor, source.Tenant))
                ?? throw new InvalidOperationException("The stored catalogue refusal must be captured.");
            await Assert.That(refusal.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(refusal.MessageText).IsEqualTo("local_administration_catalogue_guard_drift");
            await transaction.RollbackAsync();
            await Assert.ThrowsAsync<PostgresException>(() => Native.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(await HelperFingerprintAsync(migration, signature)).IsEqualTo(changed);
        }
        finally
        {
            await ExecuteCatalogueStatementAsync(migration, definition);
            await ExecuteCatalogueStatementAsync(migration,
                $"REVOKE ALL ON FUNCTION {signature} FROM PUBLIC; "
                + $"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
        await Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task CatalogueGuardCannotBeExecutedDirectlyByApplicationLogins(PrimaryDatabaseRole role)
    {
        var source = await SeedAsync();
        await using var caller = Open(role, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteCatalogueStatementAsync(caller,
                $"SELECT {Native.FunctionSql(caller, "assert_catalogue")}()"))
            ?? throw new InvalidOperationException("The private execution refusal must be captured.");
        await Assert.That(refusal.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task CatalogueGuardAcceptsTheOwnersExplicitGrantOption()
    {
        var source = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string signature = Native.FunctionSql(migration, "assert_catalogue") + "()";
        int expectedCount = await migration.Set<LocalIdentityUser>().CountAsync();
        try
        {
            await ExecuteCatalogueStatementAsync(migration,
                $"GRANT EXECUTE ON FUNCTION {signature} TO {names.Quote(Native.OwnerRole)} WITH GRANT OPTION");
            await Native.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await using var runtime = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
            await runtime.Database.OpenConnectionAsync();
            await using var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            using var image = JsonDocument.Parse(await ReadStoredCatalogueAsync(runtime, source.Actor, source.Tenant));
            await Assert.That(image.RootElement.GetProperty("Data").GetProperty("LocalIdentityUser").GetArrayLength())
                .IsEqualTo(Math.Min(expectedCount, 1));
            await transaction.RollbackAsync();
        }
        finally
        {
            await ExecuteCatalogueStatementAsync(migration,
                $"REVOKE GRANT OPTION FOR EXECUTE ON FUNCTION {signature} FROM {names.Quote(Native.OwnerRole)}");
        }
        await Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private static async Task ExecuteCatalogueStatementAsync(ExploreDbContext context, string statement)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync();
    }

    private static Task<string> HelperFingerprintAsync(ExploreDbContext context, string signature) =>
        context.Database.SqlQueryRaw<string>("""
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_array(
                f.prosrc,f.proacl::text,f.proconfig::text,f.proowner::text,
                pg_catalog.obj_description(f.oid,'pg_proc'))::text) AS "Value"
            FROM pg_catalog.pg_proc f WHERE f.oid=pg_catalog.to_regprocedure({0})
            """, signature).SingleAsync();

    private static async Task<string> ReadStoredCatalogueAsync(ExploreDbContext context, Guid actor, Guid tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {Native.FunctionSql(context, "read")}(@request)::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new Native.Request("List", LocalAdministrationStage.Disclose,
                actor, tenant, PageSize: 1))
        });
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The stored lifecycle image is absent."));
    }
}
