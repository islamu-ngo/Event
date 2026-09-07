// Defines grace period and other settings for rotating HttpClient and DbContext connections.

namespace Explore.Secrets.Configuration;

/// <summary>
/// Configuration options for connection pool rotation.
/// Controls how HttpClient and DbContext connections are rotated when credentials change.
/// </summary>
public sealed class RotationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Rotation";

    /// <summary>
    /// Grace period to wait before disposing old connections after rotation.
    /// Allows in-flight requests to complete before the old connection is disposed.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable connection rotation.
    /// When disabled, connections will not be rotated when credentials change.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to log connection string changes (credentials will be redacted).
    /// Default: true.
    /// </summary>
    public bool LogRotationEvents { get; set; } = true;

    /// <summary>
    /// Maximum number of concurrent rotations allowed.
    /// Prevents thundering herd when multiple credentials change simultaneously.
    /// Default: 5.
    /// </summary>
    public int MaxConcurrentRotations { get; set; } = 5;
}

/// <summary>
/// Options for configuring HTTP client credentials.
/// Used by RotationAwareHttpClientFactory to detect credential changes.
/// </summary>
public sealed class HttpClientCredentialOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "HttpClientCredentials";

    /// <summary>
    /// Named HTTP clients and their credential sources.
    /// Key: client name, Value: configuration path for credentials.
    /// </summary>
    public Dictionary<string, HttpClientCredential> Clients { get; set; } = new();
}

/// <summary>
/// Credentials for a named HTTP client.
/// </summary>
public sealed class HttpClientCredential
{
    /// <summary>
    /// Bearer token for Authorization header.
    /// </summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// API key for X-API-Key header.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom headers to add to requests.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Base address for the HTTP client.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Timeout for HTTP requests.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>
/// Options for database connection configuration.
/// Used by RotationAwareDbContextFactory to detect connection string changes.
/// </summary>
public sealed class DatabaseConnectionOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The connection string for the database.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Maximum number of connections in the pool.
    /// </summary>
    public int? MaxPoolSize { get; set; }

    /// <summary>
    /// Minimum number of connections in the pool.
    /// </summary>
    public int? MinPoolSize { get; set; }

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int? ConnectionTimeout { get; set; }

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    public int? CommandTimeout { get; set; }
}
