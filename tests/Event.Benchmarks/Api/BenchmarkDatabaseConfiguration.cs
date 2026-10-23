namespace Event.Benchmarks.Api;

internal static class BenchmarkDatabaseConfiguration
{
    internal static (string Username, string Password) ReadPostgreSqlLogin()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SECRET_PROVIDER"), "Environment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("benchmark_environment_secret_authority_required");
        string? username = Environment.GetEnvironmentVariable("DATABASE_RUNTIME_USERNAME");
        string? password = Environment.GetEnvironmentVariable("DATABASE_RUNTIME_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("benchmark_postgres_login_not_configured");
        return (username, password);
    }
}
