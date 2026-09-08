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

    internal static IdentityDatabaseTopology GetTopology(IConfiguration configuration)
    {
        string? value = configuration[$"{SectionName}:Topology"]?.Trim();
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

        string? providerValue = configuration[$"{SectionName}:Provider"]?.Trim();
        if (!Enum.TryParse(providerValue, true, out PrimaryDatabaseProvider provider)
            || !Enum.IsDefined(provider))
        {
            throw new InvalidOperationException(
                "IdentityDatabase:Provider must be PostgreSql, Sqlite, SqlServer, MariaDb, or MySql.");
        }

        string schema = configuration[$"{SectionName}:Schema"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(schema))
        {
            schema = DefaultSchema;
        }

        string? directConnectionString = configuration[$"{SectionName}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(directConnectionString))
        {
            return new ExternalIdentityDatabaseDescriptor(
                provider,
                directConnectionString,
                schema,
                GetServerVersion(provider, configuration),
                GetServerFlavor(provider));
        }

        string roleName = role == PrimaryDatabaseRole.Runtime ? "Runtime" : "Migrator";
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = provider,
            Host = configuration[$"{SectionName}:Host"],
            Port = ReadOptionalInt(configuration[$"{SectionName}:Port"]),
            Database = configuration[$"{SectionName}:Name"],
            Schema = schema,
            Username = configuration[$"{SectionName}:{roleName}:Username"],
            Password = configuration[$"{SectionName}:{roleName}:Password"],
            TlsMode = ReadTlsMode(configuration[$"{SectionName}:TlsMode"], provider),
            TrustServerCertificate =
                bool.TryParse(configuration[$"{SectionName}:TrustServerCertificate"], out bool trust)
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
        string? value = configuration[$"{SectionName}:ServerVersion"];
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
