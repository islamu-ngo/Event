using System.Globalization;
using Npgsql;

namespace Event.Benchmarks.Api;

internal static class BenchmarkDatabaseConfiguration
{
    public static string BuildPostgreSqlConnectionString(
        string host,
        int port,
        string database,
        string username,
        string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Prefer,
        };

        return builder.ConnectionString;
    }

    public static void AddPostgreSql(IDictionary<string, string?> configuration, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        var trustServerCertificate = parsed.TryGetValue("Trust Server Certificate", out var trustValue)
            && bool.TryParse(trustValue?.ToString(), out var trust)
            && trust;
        configuration["Database:Provider"] = "PostgreSql";
        configuration["Database:Host"] = parsed.Host;
        configuration["Database:Port"] = parsed.Port.ToString(CultureInfo.InvariantCulture);
        configuration["Database:Database"] = parsed.Database;
        configuration["Database:Runtime:Username"] = parsed.Username;
        configuration["Database:Runtime:Password"] = parsed.Password;
        configuration["Database:Runtime:TlsMode"] = parsed.SslMode switch
        {
            SslMode.Disable => "Disabled",
            SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull => "Required",
            _ => "Prefer",
        };
        configuration["Database:Runtime:TrustServerCertificate"] = trustServerCertificate.ToString();
    }
}
