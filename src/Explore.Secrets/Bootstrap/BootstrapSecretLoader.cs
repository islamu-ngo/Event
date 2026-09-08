using System.Globalization;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Bootstrap;

/// <summary>
/// Resolves the Postgres connection string during application bootstrap, before
/// <c>ExploreDbContext</c> (and therefore <c>ISecretResolver</c>) is available.
/// </summary>
/// <remarks>
/// <para>
/// This loader deliberately does NOT use <c>ISecretResolver</c> or <c>SecretBinding</c> rows:
/// the DB that holds those bindings cannot unlock its own connection-string secrets
/// (chicken-and-egg). The <c>SecretDefinitionRegistry</c> enforces the matching
/// invariant at the domain layer by marking <c>postgresql.*</c> keys as
/// <c>IsBootstrapSecret = true</c>; all values come from the selected deployment authority.
/// </para>
/// <para>
/// <c>SecretProvider:Provider</c> selects exactly one authority. Environment mode
/// reads process environment variables; Infisical mode reads only its isolated
/// provider configuration. Missing or failed Infisical values never fall back.
/// </para>
/// <para>
/// There is no <c>POSTGRESQL_PUBLIC_URL</c> fallback. The URL form is deliberately removed
/// so the connection string is always constructed by <see cref="NpgsqlConnectionStringBuilder"/>
/// with <c>SSL Mode = Prefer</c> and <c>Trust Server Certificate = true</c> (safe defaults for
/// self-hosted operators who may or may not front Postgres with a TLS terminator).
/// </para>
/// <para>
/// If any required field is missing from the selected authority, a single
/// <see cref="InvalidOperationException"/> is thrown with an actionable message listing
/// exactly which fields are missing. Startup fails loudly rather than silently producing
/// a broken connection string.
/// </para>
/// </remarks>
public static class BootstrapSecretLoader
{
    /// <summary>Canonical Infisical folder path for Postgres secrets.</summary>
    public const string InfisicalPath = "/postgresql";

    /// <summary>Expected Infisical secret key for the Postgres host.</summary>
    public const string InfisicalKeyHost = "POSTGRESQL_HOST";

    /// <summary>Expected Infisical secret key for the Postgres port.</summary>
    public const string InfisicalKeyPort = "POSTGRESQL_PORT";

    /// <summary>Expected Infisical secret key for the Postgres database name.</summary>
    public const string InfisicalKeyDatabase = "POSTGRESQL_DATABASE";

    /// <summary>Expected Infisical secret key for the Postgres username.</summary>
    public const string InfisicalKeyUsername = "POSTGRESQL_USERNAME";

    /// <summary>Expected Infisical secret key for the Postgres password.</summary>
    public const string InfisicalKeyPassword = "POSTGRESQL_PASSWORD";

    /// <summary>Environment variable for the Postgres host.</summary>
    public const string EnvHost = "POSTGRESQL_HOST";

    /// <summary>Environment variable for the Postgres port.</summary>
    public const string EnvPort = "POSTGRESQL_PORT";

    /// <summary>Environment variable for the Postgres database name.</summary>
    public const string EnvDatabase = "POSTGRESQL_DATABASE";

    /// <summary>Environment variable for the Postgres username.</summary>
    public const string EnvUsername = "POSTGRESQL_USERNAME";

    /// <summary>Environment variable for the Postgres password.</summary>
    public const string EnvPassword = "POSTGRESQL_PASSWORD";

    /// <summary>IConfiguration key for the Postgres host.</summary>
    public const string ConfigHost = "Postgresql:Host";

    /// <summary>IConfiguration key for the Postgres port.</summary>
    public const string ConfigPort = "Postgresql:Port";

    /// <summary>IConfiguration key for the Postgres database name.</summary>
    public const string ConfigDatabase = "Postgresql:Database";

    /// <summary>IConfiguration key for the Postgres username.</summary>
    public const string ConfigUsername = "Postgresql:Username";

    /// <summary>IConfiguration key for the Postgres password.</summary>
    public const string ConfigPassword = "Postgresql:Password";

    /// <summary>Default Postgres port when not supplied anywhere.</summary>
    public const int DefaultPort = 5432;

    public static void ProjectPostgresConfiguration(
        IConfigurationBuilder configBuilder,
        PrimaryDatabaseRole role)
    {
        ArgumentNullException.ThrowIfNull(configBuilder);

        var configuration = configBuilder.Build();
        var roleSection = role == PrimaryDatabaseRole.Runtime ? "Runtime" : "Migrator";
        var roleProvider = configuration[$"Database:{roleSection}:Provider"];
        var rootProvider = configuration["Database:Provider"];
        var explicitProvider = string.IsNullOrWhiteSpace(roleProvider)
            ? rootProvider
            : roleProvider;
        if (!string.IsNullOrWhiteSpace(explicitProvider)
            && !string.Equals(
                explicitProvider.Trim(),
                nameof(PrimaryDatabaseProvider.PostgreSql),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IConfiguration authority = SecretAuthorityConfiguration.Build(
            configuration,
            SecretAuthorityConfiguration.GetEnvironmentName(configuration),
            InfisicalPath);
        string source = SecretAuthorityConfiguration.GetRequiredProvider(configuration).ToString();
        var (host, _) = ResolveField(
            authority, EnvHost, ConfigHost, source);
        var (port, _) = ResolveField(
            authority, EnvPort, ConfigPort, source);
        var (database, _) = ResolveField(
            authority, EnvDatabase, ConfigDatabase, source);
        var (username, _) = ResolveField(
            authority, EnvUsername, ConfigUsername, source);
        var (password, _) = ResolveField(
            authority, EnvPassword, ConfigPassword, source);

        if (string.IsNullOrWhiteSpace(host)
            && string.IsNullOrWhiteSpace(port)
            && string.IsNullOrWhiteSpace(database)
            && string.IsNullOrWhiteSpace(username)
            && string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var projected = new Dictionary<string, string?>();

        if (string.IsNullOrWhiteSpace(roleProvider)
            && string.IsNullOrWhiteSpace(rootProvider))
        {
            projected["Database:Provider"] = nameof(PrimaryDatabaseProvider.PostgreSql);
        }

        TryProject(projected, configuration, roleSection, "Host", host);
        TryProject(projected, configuration, roleSection, "Port", port);
        TryProject(projected, configuration, roleSection, "Database", database);
        TryProject(projected, configuration, roleSection, "Username", username, roleScoped: true);
        TryProject(projected, configuration, roleSection, "Password", password, roleScoped: true);

        if (projected.Count > 0)
        {
            configBuilder.AddInMemoryCollection(projected);
        }
    }

    /// <summary>
    /// Resolves Postgres bootstrap credentials and returns a composed Npgsql connection string.
    /// </summary>
    /// <param name="configuration">Bootstrap <see cref="IConfiguration"/> (env vars + appsettings).
    /// Used both to discover Infisical bootstrap credentials and as the lowest-priority fallback
    /// for the Postgres fields themselves.</param>
    /// <param name="logger">Optional logger; loader logs the winning source per field without
    /// ever logging the password or host values.</param>
    /// <returns>A <see cref="BootstrapPostgresCredentials"/> with the composed connection string
    /// and a label describing the winning source.</returns>
    /// <exception cref="InvalidOperationException">One or more required fields could not be
    /// resolved from any source. The exception message lists the missing field names.</exception>
    public static BootstrapPostgresCredentials LoadPostgresConnectionString(
        IConfiguration configuration,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfiguration authority = SecretAuthorityConfiguration.Build(
            configuration,
            SecretAuthorityConfiguration.GetEnvironmentName(configuration),
            InfisicalPath);
        string source = SecretAuthorityConfiguration.GetRequiredProvider(configuration).ToString();

        var (host, hostSource) = ResolveField(
            authority, EnvHost, ConfigHost, source);
        var (portRaw, portSource) = ResolveField(
            authority, EnvPort, ConfigPort, source);
        var (database, databaseSource) = ResolveField(
            authority, EnvDatabase, ConfigDatabase, source);
        var (username, usernameSource) = ResolveField(
            authority, EnvUsername, ConfigUsername, source);
        var (password, passwordSource) = ResolveField(
            authority, EnvPassword, ConfigPassword, source);

        var missing = new List<string>(5);
        if (string.IsNullOrWhiteSpace(host)) missing.Add(nameof(host));
        if (string.IsNullOrWhiteSpace(database)) missing.Add(nameof(database));
        if (string.IsNullOrWhiteSpace(username)) missing.Add(nameof(username));
        if (string.IsNullOrWhiteSpace(password)) missing.Add(nameof(password));

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Postgres bootstrap credentials are incomplete. Missing required fields: "
                + string.Join(", ", missing)
                + ". Repair the selected secret authority and restart.");
        }

        var port = ParsePort(portRaw, logger);

        var winningSource = DescribeWinningSource(
            hostSource, portSource, databaseSource, usernameSource, passwordSource);

        var connectionResult = PrimaryDatabaseConfiguration.BuildConnectionString(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = host!,
            Port = port,
            Database = database!,
            Username = username!,
            Password = password!,
            TlsMode = PrimaryDatabaseTlsMode.Prefer,
            TrustServerCertificate = false,
        });

        logger?.LogInformation(
            "Bootstrap Postgres credentials resolved from {Source} (host via {HostSource}, "
            + "port via {PortSource}, database via {DatabaseSource}, username via {UsernameSource}, "
            + "password via {PasswordSource}).",
            winningSource,
            hostSource,
            portSource,
            databaseSource,
            usernameSource,
            passwordSource);

        return new BootstrapPostgresCredentials(
            connectionResult.ConnectionString,
            winningSource,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Parses a Postgres port from the resolved raw string, falling back to 5432 and
    /// logging a warning if the value is unparseable (we prefer "work with default"
    /// over "hard-fail on malformed port" for self-hosted operators).
    /// </summary>
    private static int ParsePort(string? portRaw, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(portRaw))
        {
            return DefaultPort;
        }

        if (int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and < 65536)
        {
            return port;
        }

        logger?.LogWarning(
            "Postgres bootstrap port value is not a valid TCP port. Falling back to {DefaultPort}.",
            DefaultPort);
        return DefaultPort;
    }

    /// <summary>
    /// Resolves a single field from the already-isolated authority configuration.
    /// </summary>
    private static (string? Value, string Source) ResolveField(
        IConfiguration authority,
        string envKey,
        string configKey,
        string source)
    {
        string? value = authority[envKey] ?? authority[configKey];
        return string.IsNullOrWhiteSpace(value) ? (null, source) : (value, source);
    }

    private static void TryProject(
        IDictionary<string, string?> projected,
        IConfiguration configuration,
        string roleSection,
        string key,
        string? value,
        bool roleScoped = false)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.IsNullOrWhiteSpace(configuration[$"Database:{roleSection}:{key}"])
            || !string.IsNullOrWhiteSpace(configuration[$"Database:{key}"]))
        {
            return;
        }

        var targetKey = roleScoped
            ? $"Database:{roleSection}:{key}"
            : $"Database:{key}";
        projected[targetKey] = value;
    }

    /// <summary>
    /// Produces a high-level summary of which source family supplied most fields.
    /// Matches the priority chain: Infisical > EnvironmentVariables > IConfiguration > Mixed.
    /// </summary>
    private static string DescribeWinningSource(params string[] fieldSources)
    {
        var distinctPrefixes = fieldSources
            .Select(static s => s.Split(':', 2)[0])
            .Where(static p => !string.Equals(p, "<unresolved>", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinctPrefixes.Count switch
        {
            0 => "<unresolved>",
            1 => distinctPrefixes[0],
            _ => $"Mixed({string.Join("+", distinctPrefixes)})",
        };
    }

}
