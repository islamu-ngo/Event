using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Federation;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresAtprotoModerationInstallationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("owner-member")]
    [Arguments("global-parent")]
    [Arguments("global-owner")]
    public async Task InstallerRejectsUnexpectedAuthorityBeforePublishingCapabilities(string unsafeAuthority)
    {
        await using var context = fixture.CreateDbContext();
        var entity = context.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        string role = sql.DelimitIdentifier("unsafe_moderation_" + Guid.CreateVersion7().ToString("N"));
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_get_userbyid(relowner) FROM pg_catalog.pg_class WHERE oid = @table::regclass";
        command.Parameters.AddWithValue("table", table);
        string originalOwner = sql.DelimitIdentifier((string)(await command.ExecuteScalarAsync())!);
        string restorationSql = unsafeAuthority switch
        {
            "owner-member" => $"REVOKE {PostgresAtprotoModerationCompensationContract.OwnerRole} FROM {role}",
            "global-parent" => $"REVOKE {role} FROM {PostgresAtprotoModerationCompensationContract.GlobalRole}",
            "global-owner" => $"ALTER TABLE {table} OWNER TO {originalOwner}",
            _ => throw new InvalidOperationException()
        };
        command.Parameters.Clear();
        command.CommandText = $"CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS";
        await command.ExecuteNonQueryAsync();
        try
        {
            command.CommandText = unsafeAuthority switch
            {
                "owner-member" => $"GRANT {PostgresAtprotoModerationCompensationContract.OwnerRole} TO {role}",
                "global-parent" => $"GRANT {role} TO {PostgresAtprotoModerationCompensationContract.GlobalRole}",
                "global-owner" => $"ALTER TABLE {table} OWNER TO {PostgresAtprotoModerationCompensationContract.GlobalRole}",
                _ => throw new InvalidOperationException()
            };
            await command.ExecuteNonQueryAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresAtprotoModerationCompensationContract.ApplyAsync(context));
            await Assert.That(rejected!.SqlState).IsEqualTo("P0001");
        }
        finally
        {
            command.CommandText = restorationSql;
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"DROP ROLE {role}";
            await command.ExecuteNonQueryAsync();
        }
    }
}
