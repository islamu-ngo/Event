// Carries the composed connection string and a label describing which source won.

namespace Explore.Secrets.Bootstrap;

/// <summary>
/// Result of resolving Postgres bootstrap credentials. The connection string is
/// composed from discrete fields (Host, Port, Database, Username, Password) via
/// <see cref="Npgsql.NpgsqlConnectionStringBuilder"/> so the runtime never trusts
/// URL-form secrets and cannot be confused by a stale cached URL.
/// </summary>
/// <param name="ConnectionString">Fully-composed Npgsql connection string ready to hand to
/// <c>UseNpgsql(...)</c>. Contains Password; treat as secret in logs.</param>
/// <param name="Source">Human-readable label of the winning source (for example,
/// <c>"Infisical:/postgresql"</c>, <c>"EnvironmentVariables"</c>, <c>"IConfiguration:Postgresql"</c>).
/// Used in startup logs; never logs the actual secret value.</param>
/// <param name="LoadedAt">UTC timestamp the credentials were composed. Useful to correlate
/// startup logs and to show age if credentials are rotated via re-deploy.</param>
public sealed record BootstrapPostgresCredentials(
    string ConnectionString,
    string Source,
    DateTimeOffset LoadedAt);
