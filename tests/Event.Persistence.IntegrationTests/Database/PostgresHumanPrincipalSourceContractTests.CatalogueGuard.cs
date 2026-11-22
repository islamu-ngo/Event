using System.Security.Cryptography;
using System.Text;
using Explore.Persistence.Database;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

public sealed partial class PostgresHumanPrincipalSourceContractTests
{
    [Test]
    [Arguments("body")]
    [Arguments("executor")]
    [Arguments("search_path")]
    public async Task CatalogueGuardRejectsLiveDriftAndReinstallationCannotRepairIt(string drift)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        string helper = database.Sql.Name("human_principal_source_assert_catalogue") + "()";
        const string counterfeit = "BEGIN RETURN; END;";
        string change = drift switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {helper} RETURNS void
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                COMMENT ON FUNCTION {helper} IS
                    'human-principal-source:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """,
            "executor" => $"GRANT EXECUTE ON FUNCTION {helper} TO PUBLIC",
            "search_path" => $"ALTER FUNCTION {helper} SET search_path = public, pg_catalog",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await PrincipalDatabase.ExecuteAsync(database.Migration, change);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(runtime, database.Tenant);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(runtime, database.Actor, 1, database.Tenant))
            ?? throw new InvalidOperationException("The runtime catalogue refusal must be captured.");
        await Assert.That(refusal.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await Assert.That(refusal.MessageText).IsEqualTo("human_principal_catalogue_guard_drift");
        await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task CatalogueGuardCannotBeExecutedDirectlyByApplicationLogins(PrimaryDatabaseRole role)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        string before = await database.CatalogAsync();
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            PrincipalDatabase.ExecuteAsync(caller,
                $"SELECT {database.Sql.Name("human_principal_source_assert_catalogue")}()"))
            ?? throw new InvalidOperationException("The private-executor refusal must be captured.");
        await Assert.That(refusal.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task CatalogueGuardAcceptsTheOwnersExplicitGrantOption()
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await PrincipalDatabase.ExecuteAsync(database.Migration,
            $"GRANT EXECUTE ON FUNCTION {database.Sql.Name("human_principal_source_assert_catalogue")}() "
            + "TO event_human_principal_source_owner WITH GRANT OPTION");
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(runtime, database.Tenant);
        var facts = await database.ReadAsync(runtime, database.Actor, 1, database.Tenant);
        await Assert.That(facts.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
        await database.InstallAsync();
    }
}
