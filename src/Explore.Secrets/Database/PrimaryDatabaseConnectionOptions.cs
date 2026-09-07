namespace Explore.Secrets.Database;

public sealed record PrimaryDatabaseConnectionOptions
{
    public const string DefaultSchema = "islamu_event";

    public required PrimaryDatabaseRole Role { get; init; }

    public required PrimaryDatabaseProvider Provider { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? Database { get; init; }

    public string Schema { get; init; } = DefaultSchema;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public PrimaryDatabaseTlsMode TlsMode { get; init; } = PrimaryDatabaseTlsMode.Prefer;

    public bool TrustServerCertificate { get; init; }

    public PrimaryDatabaseServerFlavor? ServerFlavor { get; init; }

    public Version? ServerVersion { get; init; }
}
