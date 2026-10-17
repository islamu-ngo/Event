using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class OutboxAdmissionExecuteAclTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task UnknownExecutorSurvivesFailedReinstallAndBlocksAdmission(bool notification, bool read)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        async Task InstallAsync()
        {
            if (notification)
                await PostgresNotificationOutboxAdmissionContract.ApplyAsync(context);
            else
                await PostgresGenericOutboxAdmissionContract.ApplyAsync(context);
        }
        await InstallAsync();
        string function = notification
            ? (read ? PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context)
                : PostgresNotificationOutboxAdmissionContract.ClaimFunctionSql(context))
            : (read ? PostgresGenericOutboxAdmissionContract.ReadFunctionSql(context)
                : PostgresGenericOutboxAdmissionContract.ClaimFunctionSql(context));
        string signature = read
            ? function + (notification ? "(uuid, timestamp with time zone)" : "(uuid, timestamp with time zone, integer)")
            : function + "(integer, timestamp with time zone)";
        string call = read
            ? function + (notification ? "(NULL::uuid, now())" : "(NULL::uuid, now(), 1)")
            : function + "(1, now())";
        string outsider = $"admission_acl_{Guid.CreateVersion7():N}";
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE ROLE \"{outsider}\" NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS");
        await context.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {signature} TO \"{outsider}\"");
        try
        {
            var entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                await context.Database.ExecuteSqlRawAsync($"SELECT * FROM {call}"));
            await Assert.That(entry!.SqlState).IsEqualTo("55000");
            var reinstall = await Assert.ThrowsAsync<PostgresException>(InstallAsync);
            await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
            bool retained = await context.Database.SqlQueryRaw<bool>(
                "SELECT pg_catalog.has_function_privilege({0}, {1}, 'EXECUTE') AS \"Value\"",
                outsider, signature).SingleAsync();
            await Assert.That(retained).IsTrue();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{outsider}\"");
            await context.Database.ExecuteSqlRawAsync($"DROP ROLE \"{outsider}\"");
        }
        await InstallAsync();
        await context.Database.ExecuteSqlRawAsync($"SELECT * FROM {call}");
    }
}
