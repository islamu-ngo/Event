using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Database;

public static class PrivacyErasureAuthorityDatabaseConfiguration
{
    public const string SectionName = "PrivacyErasureAuthorityDatabase";
    public const string MigrationsHistoryTable = "__EFPrivacyErasureAuthorityMigrationsHistory";

    private const string EnvironmentPrefix = "PRIVACY_ERASURE_AUTHORITY_";

    public static PrimaryDatabaseConnectionOptions BindRuntime(IConfiguration configuration) =>
        Bind(configuration, PrimaryDatabaseRole.Runtime);

    public static PrimaryDatabaseConnectionOptions BindMigrator(IConfiguration configuration) =>
        Bind(configuration, PrimaryDatabaseRole.Migrator);

    public static PrimaryDatabaseConnectionOptions Bind(
        IConfiguration configuration,
        PrimaryDatabaseRole role)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureDistinctRoleUsernames(configuration);

        IConfiguration projected = BuildPrimaryProjection(configuration);
        PrimaryDatabaseConnectionOptions options;
        try
        {
            options = PrimaryDatabaseConfiguration.Bind(projected, role);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OptionsValidationException)
        {
            throw InvalidConfiguration(exception.Message);
        }

        if (options.Provider != PrimaryDatabaseProvider.PostgreSql)
        {
            throw InvalidConfiguration($"{SectionName}:Provider must be PostgreSql.");
        }

        return options;
    }

    private static void EnsureDistinctRoleUsernames(IConfiguration configuration)
    {
        string? runtimeUsername = ReadRole(
            configuration,
            "Runtime",
            "Username",
            "RUNTIME_USERNAME");
        string? migratorUsername = ReadRole(
            configuration,
            "Migrator",
            "Username",
            "MIGRATOR_USERNAME");
        if (!string.IsNullOrWhiteSpace(runtimeUsername)
            && !string.IsNullOrWhiteSpace(migratorUsername)
            && string.Equals(
                runtimeUsername.Trim(),
                migratorUsername.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidConfiguration(
                "Runtime and Migrator usernames must be distinct.");
        }
    }

    public static PrimaryDatabaseConnectionResult ResolveRuntimeConnectionString(
        IConfiguration configuration) =>
        PrimaryDatabaseConfiguration.BuildConnectionString(BindRuntime(configuration));

    public static PrimaryDatabaseConnectionResult ResolveMigratorConnectionString(
        IConfiguration configuration) =>
        PrimaryDatabaseConfiguration.BuildConnectionString(BindMigrator(configuration));

    public static void EnsureDistinctPhysicalDatabase(
        PrimaryDatabaseConnectionOptions application,
        PrimaryDatabaseConnectionOptions authority)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(authority);

        if (application.Provider != PrimaryDatabaseProvider.PostgreSql
            || authority.Provider != PrimaryDatabaseProvider.PostgreSql)
        {
            return;
        }

        if (application.Port == authority.Port
            && string.Equals(
                NormalizeHost(application.Host ?? string.Empty),
                NormalizeHost(authority.Host ?? string.Empty),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                application.Database,
                authority.Database,
                StringComparison.Ordinal))
        {
            throw InvalidConfiguration(
                "ExternalDatabase requires the privacy-erasure authority and application migrations to target a different physical PostgreSQL database.");
        }
    }

    public static void ProjectDiscreteConfiguration(IConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        IConfiguration configuration = configurationBuilder.Build();
        Dictionary<string, string?> projected = DiscreteProjection(configuration);
        if (projected.Count > 0)
        {
            configurationBuilder.AddInMemoryCollection(projected);
        }
    }

    private static IConfiguration BuildPrimaryProjection(IConfiguration configuration)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = Read(configuration, "Provider", discreteSuffix: null),
            ["Database:Host"] = Read(configuration, "Host", "HOST"),
            ["Database:Port"] = Read(configuration, "Port", "PORT"),
            ["Database:Database"] = Read(configuration, "Database", "DATABASE"),
            ["Database:TlsMode"] = Read(configuration, "TlsMode", "TLS_MODE"),
            ["Database:TrustServerCertificate"] = Read(
                configuration,
                "TrustServerCertificate",
                "TRUST_SERVER_CERTIFICATE"),
            ["Database:Runtime:Username"] = ReadRole(configuration, "Runtime", "Username", "RUNTIME_USERNAME"),
            ["Database:Runtime:Password"] = ReadRole(configuration, "Runtime", "Password", "RUNTIME_PASSWORD"),
            ["Database:Migrator:Username"] = ReadRole(configuration, "Migrator", "Username", "MIGRATOR_USERNAME"),
            ["Database:Migrator:Password"] = ReadRole(configuration, "Migrator", "Password", "MIGRATOR_PASSWORD"),
        };

        if (string.IsNullOrWhiteSpace(values["Database:Provider"])
            && values.Any(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            values["Database:Provider"] = nameof(PrimaryDatabaseProvider.PostgreSql);
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Dictionary<string, string?> DiscreteProjection(IConfiguration configuration)
    {
        var projected = new Dictionary<string, string?>();
        Project(projected, configuration, "Host", "HOST");
        Project(projected, configuration, "Port", "PORT");
        Project(projected, configuration, "Database", "DATABASE");
        Project(projected, configuration, "TlsMode", "TLS_MODE");
        Project(projected, configuration, "TrustServerCertificate", "TRUST_SERVER_CERTIFICATE");
        ProjectRole(projected, configuration, "Runtime", "Username", "RUNTIME_USERNAME");
        ProjectRole(projected, configuration, "Runtime", "Password", "RUNTIME_PASSWORD");
        ProjectRole(projected, configuration, "Migrator", "Username", "MIGRATOR_USERNAME");
        ProjectRole(projected, configuration, "Migrator", "Password", "MIGRATOR_PASSWORD");

        if (projected.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(configuration["Database:Erasure:Provider"]))
            {
                projected["Database:Erasure:Provider"] = nameof(PrimaryDatabaseProvider.PostgreSql);
            }
            if (string.IsNullOrWhiteSpace(configuration[$"{SectionName}:Provider"]))
            {
                projected[$"{SectionName}:Provider"] = nameof(PrimaryDatabaseProvider.PostgreSql);
            }
        }

        return projected;
    }

    private static string? Read(
        IConfiguration configuration,
        string field,
        string? discreteSuffix,
        string? defaultValue = null)
    {
        string? explicitValue = configuration[$"Database:Erasure:{field}"]
            ?? configuration[$"DatabaseErasure:{field}"]
            ?? configuration[$"{SectionName}:{field}"];
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        if (discreteSuffix is null)
        {
            return defaultValue;
        }

        return configuration[$"DATABASE_ERASURE_{discreteSuffix}"]
            ?? configuration[$"DATABASE_ERASURE_DATABASE_{discreteSuffix}"]
            ?? configuration[$"ERASURE_DATABASE_{discreteSuffix}"]
            ?? configuration[$"ERASURE_{discreteSuffix}"]
            ?? configuration[$"{EnvironmentPrefix}{discreteSuffix}"]
            ?? (discreteSuffix == "DATABASE" ? (configuration["DATABASE_ERASURE_NAME"] ?? configuration["ERASURE_DATABASE_NAME"] ?? configuration["ERASURE_NAME"]) : null)
            ?? defaultValue;
    }

    private static string? ReadRole(
        IConfiguration configuration,
        string role,
        string field,
        string discreteSuffix)
    {
        string? explicitValue = configuration[$"Database:Erasure:{role}:{field}"]
            ?? configuration[$"DatabaseErasure:{role}:{field}"]
            ?? configuration[$"{SectionName}:{role}:{field}"];
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        return configuration[$"DATABASE_ERASURE_{role.ToUpperInvariant()}_{field.ToUpperInvariant()}"]
            ?? configuration[$"DATABASE_ERASURE_{discreteSuffix}"]
            ?? configuration[$"ERASURE_DATABASE_{discreteSuffix}"]
            ?? configuration[$"ERASURE_{discreteSuffix}"]
            ?? configuration[$"{EnvironmentPrefix}{discreteSuffix}"];
    }

    private static void Project(
        IDictionary<string, string?> projected,
        IConfiguration configuration,
        string field,
        string discreteSuffix)
    {
        string target = $"{SectionName}:{field}";
        string aliasTarget = $"Database:Erasure:{field}";
        string? value = Read(configuration, field, discreteSuffix);
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (string.IsNullOrWhiteSpace(configuration[target]))
            {
                projected[target] = value;
            }
            if (string.IsNullOrWhiteSpace(configuration[aliasTarget]))
            {
                projected[aliasTarget] = value;
            }
        }
    }

    private static void ProjectRole(
        IDictionary<string, string?> projected,
        IConfiguration configuration,
        string role,
        string field,
        string discreteSuffix)
    {
        string target = $"{SectionName}:{role}:{field}";
        string aliasTarget = $"Database:Erasure:{role}:{field}";
        string? value = ReadRole(configuration, role, field, discreteSuffix);
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (string.IsNullOrWhiteSpace(configuration[target]))
            {
                projected[target] = value;
            }
            if (string.IsNullOrWhiteSpace(configuration[aliasTarget]))
            {
                projected[aliasTarget] = value;
            }
        }
    }

    private static string NormalizeHost(string host)
    {
        string normalized = host.Trim().TrimEnd('.');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(normalized, out IPAddress? address)
                && IPAddress.IsLoopback(address)))
        {
            return "loopback";
        }

        return normalized;
    }

    private static OptionsValidationException InvalidConfiguration(string failure) =>
        new(SectionName, typeof(PrimaryDatabaseConnectionOptions), [failure]);
}
