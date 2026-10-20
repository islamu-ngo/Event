using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Identity;

public enum IdentityDatabaseTopology
{
    Colocated = 1,
    External = 2,
}

internal static class IdentityDatabaseConfiguration
{
    internal const string SectionName = "IdentityDatabase";
    internal const string DefaultSchema = "islamu_identity";

    /// <summary>Resolves only the declared username for a supported external Identity purpose.</summary>
    internal static string BindUsername(IConfiguration configuration, PrimaryDatabaseRole role) =>
        TryBindUsername(configuration, role, out string? username)
            ? username! : throw new InvalidOperationException($"Missing explicit IdentityDatabase:{role}:Username authority.");

    internal static bool TryBindUsername(IConfiguration configuration, PrimaryDatabaseRole role, out string? username)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var (roleName, credentialAlias) = RoleAuthority(role);
        string? declared = Read(configuration, $"{roleName}:Username", $"{credentialAlias}_USERNAME");
        username = null;
        if (declared is null or "") return false;
        if (declared != declared.Trim() || declared.Any(char.IsControl))
            throw new InvalidOperationException($"IdentityDatabase:{role}:Username must preserve an exact declared login without surrounding whitespace or control characters.");
        username = declared;
        return true;
    }

    internal static IdentityDatabaseTopology GetTopology(IConfiguration configuration)
    {
        string? value = Read(configuration, "Topology", "TOPOLOGY")?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("colocated", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityDatabaseTopology.Colocated;
        }

        return value.Equals("external", StringComparison.OrdinalIgnoreCase)
            ? IdentityDatabaseTopology.External
            : throw new InvalidOperationException(
                "IdentityDatabase:Topology must be 'colocated' or 'external'.");
    }

    internal static ExternalIdentityDatabaseDescriptor BindExternal(
        IConfiguration configuration,
        PrimaryDatabaseRole role)
    {
        if (GetTopology(configuration) != IdentityDatabaseTopology.External)
        {
            throw new InvalidOperationException(
                "External Identity database configuration is available only when IdentityDatabase:Topology is external.");
        }

        string? providerValue = Read(configuration, "Provider", "PROVIDER")?.Trim();
        if (!Enum.TryParse(providerValue, true, out PrimaryDatabaseProvider provider)
            || !Enum.IsDefined(provider))
        {
            throw new InvalidOperationException(
                "IdentityDatabase:Provider must be PostgreSql, Sqlite, SqlServer, MariaDb, or MySql.");
        }

        string schema = Read(configuration, "Schema", "SCHEMA")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(schema))
        {
            schema = DefaultSchema;
        }

        var (roleName, credentialAlias) = RoleAuthority(role);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = provider,
            Host = Read(configuration, "Host", "HOST"),
            Port = ReadOptionalInt(Read(configuration, "Port", "PORT")),
            Database = Read(configuration, "Name", "NAME"),
            Schema = schema,
            Username = Read(configuration, $"{roleName}:Username", $"{credentialAlias}_USERNAME"),
            Password = Read(configuration, $"{roleName}:Password", $"{credentialAlias}_PASSWORD"),
            TlsMode = ReadTlsMode(Read(configuration, "TlsMode", "TLS_MODE"), provider),
            TrustServerCertificate =
                bool.TryParse(Read(configuration, "TrustServerCertificate", "TRUST_SERVER_CERTIFICATE"), out bool trust)
                && trust,
            ServerFlavor = GetServerFlavor(provider),
            ServerVersion = GetServerVersion(provider, configuration),
        };
        PrimaryDatabaseConnectionResult connection = PrimaryDatabaseConfiguration.BuildConnectionString(options);
        return new ExternalIdentityDatabaseDescriptor(
            provider,
            connection.ConnectionString,
            schema,
            options.ServerVersion,
            options.ServerFlavor);
    }

    private static string? Read(IConfiguration configuration, string name, string alias) =>
        configuration[$"{SectionName}:{name}"] ?? configuration[$"IDENTITY_DATABASE_{alias}"];

    private static (string Name, string Alias) RoleAuthority(PrimaryDatabaseRole role) => role switch
    {
        PrimaryDatabaseRole.Runtime => ("Runtime", "RUNTIME"),
        PrimaryDatabaseRole.Migrator => ("Migrator", "MIGRATOR"),
        PrimaryDatabaseRole.LocalIdentityWriter => ("LocalIdentityWriter", "LOCAL_IDENTITY_WRITER"),
        _ => throw new InvalidOperationException("The selected purpose cannot access the external Identity database.")
    };

    private static int? ReadOptionalInt(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : throw new InvalidOperationException("IdentityDatabase:Port must be a valid port number.");

    private static PrimaryDatabaseTlsMode ReadTlsMode(
        string? value,
        PrimaryDatabaseProvider provider)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return provider == PrimaryDatabaseProvider.Sqlite
                ? PrimaryDatabaseTlsMode.Prefer
                : PrimaryDatabaseTlsMode.Required;
        }

        return Enum.TryParse(value, true, out PrimaryDatabaseTlsMode mode)
            ? mode
            : throw new InvalidOperationException(
                "IdentityDatabase:TlsMode must be Prefer, Required, or Disabled.");
    }

    private static PrimaryDatabaseServerFlavor? GetServerFlavor(PrimaryDatabaseProvider provider) =>
        provider switch
        {
            PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
            PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
            _ => null,
        };

    private static Version? GetServerVersion(
        PrimaryDatabaseProvider provider,
        IConfiguration configuration)
    {
        string? value = Read(configuration, "ServerVersion", "SERVER_VERSION");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return Version.TryParse(value, out Version? version)
                ? version
                : throw new InvalidOperationException(
                    "IdentityDatabase:ServerVersion must be a valid version.");
        }

        return provider switch
        {
            PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
            PrimaryDatabaseProvider.MySql => new Version(8, 4),
            _ => null,
        };
    }
}

internal sealed record ExternalIdentityDatabaseDescriptor(
    PrimaryDatabaseProvider Provider,
    string ConnectionString,
    string Schema,
    Version? ServerVersion,
    PrimaryDatabaseServerFlavor? ServerFlavor);
