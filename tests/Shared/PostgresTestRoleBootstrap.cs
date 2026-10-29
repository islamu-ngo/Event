using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Npgsql;

namespace Event.Testing.PostgreSql;

/// <summary>Provisions declared native test roles before application hosting starts.</summary>
internal static class PostgresTestRoleBootstrap
{
    internal static async Task ProvisionAsync(string administratorConnectionString,
        PostgresNativeQueueEnrollment enrollment,
        IReadOnlyDictionary<PrimaryDatabaseRole, (string Username, string Password)> purposeLogins,
        CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        await using var connection = new NpgsqlConnection(administratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (role, login) in purposeLogins)
        {
            await using var command = connection.CreateCommand();
            string username = "\"" + login.Username.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            string password = "'" + login.Password.Replace("'", "''", StringComparison.Ordinal) + "'";
            string attributes = role == PrimaryDatabaseRole.Migrator
                ? "SUPERUSER CREATEDB CREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS"
                : "NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS";
            command.CommandText = $"CREATE ROLE {username} LOGIN {attributes} PASSWORD {password}";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
