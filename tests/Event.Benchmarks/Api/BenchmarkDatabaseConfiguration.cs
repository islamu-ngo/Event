using System.Net;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Event.Benchmarks.Api;

internal static class BenchmarkDatabaseConfiguration
{
    internal static (string Username, string Password) ReadPostgreSqlMigratorLogin()
    {
        RequireEnvironmentAuthority();
        string? username = Environment.GetEnvironmentVariable("DATABASE_MIGRATOR_USERNAME");
        string? password = Environment.GetEnvironmentVariable("DATABASE_MIGRATOR_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("benchmark_postgres_migrator_not_configured");
        return (username, password);
    }

    internal static BenchmarkPostgreSqlAuthority BindOwnedPostgreSql(string ownedConnectionString)
    {
        RequireEnvironmentAuthority();
        var owned = new NpgsqlConnectionStringBuilder(ownedConnectionString);
        if (!IPAddress.TryParse(owned.Host, out var address) || !IPAddress.IsLoopback(address)
            || string.IsNullOrWhiteSpace(owned.Database) || owned.Port is < 1 or > 65535)
            throw new InvalidOperationException("benchmark_owned_postgres_endpoint_required");

        var settings = new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["Database:Provider"] = PrimaryDatabaseProvider.PostgreSql.ToString(),
            ["Database:Host"] = owned.Host,
            ["Database:Port"] = owned.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Database:Database"] = owned.Database,
            ["Database:Schema"] = PrimaryDatabaseConnectionOptions.DefaultSchema,
            ["Database:TlsMode"] = PrimaryDatabaseTlsMode.Disabled.ToString()
        };
        foreach (var role in Enum.GetValues<PrimaryDatabaseRole>())
        {
            foreach (var field in new[] { "Provider", "Host", "Port", "Database", "Schema", "TlsMode" })
                settings[$"Database:{role}:{field}"] = settings[$"Database:{field}"];
        }

        var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddEnvironmentVariables().AddInMemoryCollection(settings).Build();
        try
        {
            var roles = Enum.GetValues<PrimaryDatabaseRole>();
            if (roles.Any(role => configuration[$"Database:{role}:Username"] is not null
                || configuration[$"Database:{role}:Password"] is not null))
                throw new InvalidOperationException("benchmark_flat_role_environment_authority_required");
            var options = roles.ToDictionary(role => role,
                role => PrimaryDatabaseConfiguration.Bind(configuration, role));
            if (options.Values.Any(option => string.IsNullOrWhiteSpace(option.Username) || string.IsNullOrWhiteSpace(option.Password))
                || options.Values.Select(option => option.Username).Distinct(StringComparer.Ordinal).Count() != options.Count
                || options.Values.Select(option => option.Password).Distinct(StringComparer.Ordinal).Count() != options.Count
                || !string.Equals(options[PrimaryDatabaseRole.Migrator].Username, owned.Username, StringComparison.Ordinal)
                || !string.Equals(options[PrimaryDatabaseRole.Migrator].Password, owned.Password, StringComparison.Ordinal))
                throw new InvalidOperationException("benchmark_postgres_purpose_authority_invalid");
            var enrollment = PostgresNativeQueueEnrollment.Bind(configuration);
            _ = PlatformApiKeySourceEnrollment.Bind(configuration);
            return new BenchmarkPostgreSqlAuthority(configuration, options, enrollment);
        }
        catch
        {
            configuration.Dispose();
            throw;
        }
    }

    private static void RequireEnvironmentAuthority()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SECRET_PROVIDER"), "Environment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("benchmark_environment_secret_authority_required");
        foreach (var (name, expected) in new[]
        {
            ("AUTHENTICATION_PROVIDER", "Local"), ("AUTHORIZATION_PROVIDER", "Local"),
            ("DEPLOYMENT_MODE", "MultiTenant"), ("IDENTITY_DATABASE_TOPOLOGY", "colocated")
        })
            if (!string.Equals(Environment.GetEnvironmentVariable(name), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("benchmark_native_environment_profile_required");

        var factoryOwnedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authentication:Provider", "Authorization:Provider", "Deployment:Mode", "IdentityDatabase:Topology",
            "INSTANCE_BOOTSTRAP_MODE", "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT",
            "INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
            "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME",
            "Instance:Bootstrap:Mode", "Instance:Bootstrap:AdminProvider", "Instance:Bootstrap:AdminSubject",
            "Instance:Bootstrap:AdminEmail", "Instance:Bootstrap:BindingGeneration",
            "Instance:Bootstrap:AdminFirstName", "Instance:Bootstrap:AdminLastName",
            "AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH", "AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256",
            "Authentication:Local:PasswordBlocklistPath", "Authentication:Local:PasswordBlocklistSha256"
        };
        foreach (string name in Environment.GetEnvironmentVariables().Keys)
        {
            string normalized = name.Replace("__", ":", StringComparison.Ordinal);
            bool externalStore = new[]
            {
                "PrivacyErasure:", "PrivacyErasureAuthorityEmbedded:", "PrivacyErasureAuthorityDatabase:",
                "Database:Erasure:", "DatabaseErasure:", "ERASURE_DATABASE_", "PRIVACY_ERASURE_AUTHORITY_",
                "IdentityDatabase:"
            }.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            bool externalIdentityAlias = normalized.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("IDENTITY_DATABASE_TOPOLOGY", StringComparison.OrdinalIgnoreCase);
            if (externalStore || externalIdentityAlias || factoryOwnedAliases.Contains(normalized))
                throw new InvalidOperationException("benchmark_native_factory_owned_environment_alias_rejected");
        }
    }
}

/// <summary>Owns one immutable binding snapshot; credentials never become benchmark job metadata.</summary>
internal sealed class BenchmarkPostgreSqlAuthority(
    ConfigurationRoot configuration,
    IReadOnlyDictionary<PrimaryDatabaseRole, PrimaryDatabaseConnectionOptions> options,
    PostgresNativeQueueEnrollment enrollment) : IDisposable
{
    internal IConfiguration Configuration => configuration;
    internal PostgresNativeQueueEnrollment Enrollment => enrollment;
    internal PrimaryDatabaseConnectionOptions Migrator => options[PrimaryDatabaseRole.Migrator];
    internal PrimaryDatabaseConnectionOptions ForRole(PrimaryDatabaseRole role) => options[role];

    internal IReadOnlyDictionary<PrimaryDatabaseRole, (string Username, string Password)> CreationLogins() =>
        options.Where(pair => pair.Key != PrimaryDatabaseRole.Migrator)
            .ToDictionary(pair => pair.Key, pair => (pair.Value.Username!, pair.Value.Password!));

    public void Dispose() => configuration.Dispose();
}
