using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationProviderSubscriptionClaimAuthorityTests(PostgreSqlContainerFixture database)
{
    [Test]
    public async Task NativeAdmissionExportsOnlyOriginalCustodyAndBindingLineageColumns()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        await using var global = await fixture.Global.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {PostgresRegistrationProviderSubscriptionClaimContract.FunctionSql(global)}(2, 1, @at, interval '2 minutes')";
        var timestamp = command.CreateParameter();
        timestamp.ParameterName = "at";
        timestamp.Value = fixture.Clock.GetUtcNow().UtcDateTime;
        command.Parameters.Add(timestamp);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray()).IsEquivalentTo(new[]
        {
            "state_id", "tenant_id", "registration_provider_binding_id",
            "lease_token", "processing_generation", "lease_expires_at"
        });
        await Assert.That(await reader.ReadAsync()).IsTrue();
        var claim = new RegistrationProviderSubscriptionClaim(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetInt64(4), reader.GetDateTime(5));
        await Assert.That(await reader.ReadAsync()).IsFalse();
        await reader.DisposeAsync();
        var state = await fixture.ReadAsync(claim);
        await Assert.That(state.Id).IsEqualTo(claim.StateId);
        await Assert.That(state.LeaseToken).IsEqualTo(claim.LeaseToken);
        await Assert.That(state.ProcessingGeneration).IsEqualTo(claim.ProcessingGeneration);
        await Assert.That(state.LeaseExpiresAt).IsEqualTo(claim.LeaseExpiresAt);
        await fixture.RequireGlobalPrivateDenialsAsync();
    }

    [Test]
    public async Task OrdinaryRuntimeCannotClaimOrAssumeSubscriptionPurposeRoles()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        var claim = (await fixture.ClaimAsync(RegistrationProviderSubscriptionOperation.Sweep)).Single();
        await using var runtime = await fixture.Runtime.CreateDbContextAsync(claim.TenantId);
        string function = PostgresRegistrationProviderSubscriptionClaimContract.FunctionSql(runtime);
        await Assert.That(async () => await runtime.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {function}(2, 1, now(), interval '1 minute')"))
            .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "42501");
        foreach (string role in new[]
        {
            PostgresRegistrationProviderSubscriptionClaimContract.OwnerRole,
            PostgresRegistrationProviderSubscriptionClaimContract.GlobalRole,
            PostgresRegistrationProviderSubscriptionClaimContract.MigratorRole
        })
            await Assert.That(async () => await runtime.Database.ExecuteSqlRawAsync($"SET ROLE {role}"))
                .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "42501");
        await fixture.RequireGlobalPrivateDenialsAsync();
    }

    [Test]
    public async Task ExecutorMembershipDriftFailsBeforeBoundsAndIsNotRepairedByReinstall()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        await using var global = await fixture.Global.CreateDbContextAsync();
        await using var administrator = database.CreateDbContext();
        string function = PostgresRegistrationProviderSubscriptionClaimContract.FunctionSql(global);
        string role = PostgresRegistrationProviderSubscriptionClaimContract.GlobalRole;
        string login = database.NativeQueueEnrollment.GlobalLogin;
        await global.Database.OpenConnectionAsync();
        await administrator.Database.ExecuteSqlRawAsync(
            $"GRANT {role} TO \"{login}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await Assert.That(async () => await global.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {function}(NULL, NULL, NULL, NULL)"))
                .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "55000");
            await Assert.That(async () => await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(
                administrator, database.NativeQueueEnrollment))
                .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "55000");
            bool remains = await administrator.Database.SqlQueryRaw<bool>("""
                SELECT edge.set_option AS "Value" FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                WHERE parent.rolname = {0} AND member.rolname = {1}
                """, role, login).SingleAsync();
            await Assert.That(remains).IsTrue();
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(
                $"GRANT {role} TO \"{login}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
        }
    }

    [Test]
    public async Task ReinstallCannotAdoptDifferentDeclaredGlobalLogin()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        await using var administrator = database.CreateDbContext();
        var enrollment = database.NativeQueueEnrollment;
        var swapped = enrollment with { GlobalLogin = enrollment.RuntimeLogin, RuntimeLogin = enrollment.GlobalLogin };
        await Assert.That(async () => await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(administrator, swapped))
            .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "55000");
        await Assert.That(await fixture.ClaimAsync(RegistrationProviderSubscriptionOperation.Sweep)).HasSingleItem();
    }

    [Test]
    public async Task AdmissionBoundsRejectInvalidOperationAndOversizedLeaseWithoutClaiming()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        await using var global = await fixture.Global.CreateDbContextAsync();
        string function = PostgresRegistrationProviderSubscriptionClaimContract.FunctionSql(global);
        foreach (string arguments in new[]
        {
            "3, 1, now(), interval '1 minute'",
            "2, 1001, now(), interval '1 minute'",
            "2, 1, now(), interval '3 minutes'"
        })
            await Assert.That(async () => await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}({arguments})"))
                .Throws<PostgresException>().And.Satisfies(error => error != null && error.SqlState == "22023");
        await Assert.That(await fixture.ClaimAsync(RegistrationProviderSubscriptionOperation.Sweep)).HasSingleItem();
    }
}
