// Includes version information and timestamps for audit purposes.

namespace Explore.Secrets.Abstractions;

/// <summary>
/// Represents a secret value with optional metadata.
/// </summary>
/// <param name="Value">The secret value (plaintext).</param>
/// <param name="Version">Optional version identifier from the secret manager.</param>
/// <param name="CreatedAt">When the secret was created (if available).</param>
/// <param name="ExpiresAt">When the secret expires (if applicable).</param>
public sealed record SecretValue(
    string Value,
    string? Version = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Health status information for a secret provider.
/// </summary>
/// <param name="IsHealthy">Whether the provider is functioning correctly.</param>
/// <param name="ProviderType">The type of secret provider.</param>
/// <param name="LastSuccessfulRefresh">When secrets were last successfully refreshed.</param>
/// <param name="ConsecutiveFailures">Number of consecutive refresh failures.</param>
/// <param name="ErrorMessage">Last error message if unhealthy.</param>
public sealed record ProviderHealthInfo(
    bool IsHealthy,
    SecretProviderType ProviderType,
    DateTimeOffset? LastSuccessfulRefresh = null,
    int ConsecutiveFailures = 0,
    string? ErrorMessage = null);
