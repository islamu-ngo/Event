using Explore.Secrets.Abstractions;

namespace Explore.Secrets.Configuration;

/// <summary>
/// Configuration options for the secret provider system.
/// Bound from "SecretProvider" configuration section.
/// </summary>
public sealed class SecretProviderOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "SecretProvider";

    /// <summary>
    /// The type of secret provider to use.
    /// Must explicitly select Environment, Infisical, or UserSecrets.
    /// </summary>
    public SecretProviderType Provider { get; set; } = SecretProviderType.Unspecified;

    /// <summary>
    /// Whether to fail fast on initialization errors in production.
    /// When true, application won't start if secrets are unavailable.
    /// </summary>
    public bool FailFast { get; set; } = true;

    /// <summary>
    /// Infisical-specific settings.
    /// </summary>
    public InfisicalOptions Infisical { get; set; } = new();

}

/// <summary>
/// Infisical secret manager configuration.
/// </summary>
public sealed class InfisicalOptions
{
    /// <summary>
    /// Infisical server URL (e.g., "https://infisical.example.com").
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Infisical project ID.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Client ID for Universal Auth.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client secret for Universal Auth.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Environment slug (e.g., "dev", "staging", "prod").
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Secret paths to load (e.g., "/api", "/keycloak").
    /// If empty, loads from root path "/".
    /// </summary>
    public List<string> Paths { get; set; } = new() { "/" };
}
