using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Database;

public sealed record EmbeddedPrivacyErasureAuthorityOptions
{
    public const string SectionName = "PrivacyErasureAuthorityEmbedded";
    public const string DefaultPath = "/app/data/privacy_erasure_authority.db";
    public const int DefaultBusyTimeoutSeconds = 30;

    public required string Path { get; init; }

    public int WriterReplicaCount { get; init; } = 1;

    public int BusyTimeoutSeconds { get; init; } = DefaultBusyTimeoutSeconds;

    public static EmbeddedPrivacyErasureAuthorityOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string configuredPath = configuration[$"{SectionName}:Path"] ?? DefaultPath;
        int writerReplicaCount = ReadInt(configuration, "WriterReplicaCount", 1);
        int busyTimeoutSeconds = ReadInt(
            configuration,
            "BusyTimeoutSeconds",
            DefaultBusyTimeoutSeconds);

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(configuredPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw InvalidConfiguration("The embedded authority Path must be a valid absolute local file path.");
        }

        var failures = new List<string>();
        if (!System.IO.Path.IsPathFullyQualified(configuredPath)
            || configuredPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || configuredPath.Contains("://", StringComparison.Ordinal)
            || configuredPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            failures.Add("Path must be an absolute local filesystem path, not a URI or network path.");
        }
        if (string.IsNullOrWhiteSpace(System.IO.Path.GetFileName(fullPath)))
        {
            failures.Add("Path must identify a dedicated SQLite file.");
        }
        if (writerReplicaCount != 1)
        {
            failures.Add("WriterReplicaCount must be exactly 1 for EmbeddedSqlite.");
        }
        if (busyTimeoutSeconds is < 1 or > 300)
        {
            failures.Add("BusyTimeoutSeconds must be between 1 and 300.");
        }
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                SectionName,
                typeof(EmbeddedPrivacyErasureAuthorityOptions),
                failures);
        }

        return new EmbeddedPrivacyErasureAuthorityOptions
        {
            Path = fullPath,
            WriterReplicaCount = writerReplicaCount,
            BusyTimeoutSeconds = busyTimeoutSeconds,
        };
    }

    public string BuildConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = Path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        DefaultTimeout = BusyTimeoutSeconds,
        Pooling = true,
    }.ConnectionString;

    private static int ReadInt(
        IConfiguration configuration,
        string field,
        int defaultValue)
    {
        string? value = configuration[$"{SectionName}:{field}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed))
        {
            return parsed;
        }

        throw InvalidConfiguration($"{field} must be an integer.");
    }

    private static OptionsValidationException InvalidConfiguration(string failure) =>
        new(SectionName, typeof(EmbeddedPrivacyErasureAuthorityOptions), [failure]);
}
