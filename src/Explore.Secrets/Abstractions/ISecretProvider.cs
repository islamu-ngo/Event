// Abstracts secret retrieval from various backends (Infisical, Vault, Azure KV, AWS SM).

namespace Explore.Secrets.Abstractions;

/// <summary>
/// Provides secret retrieval from external secret managers.
/// Implementations handle authentication, caching, and refresh.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Gets the provider type for identification and logging.
    /// </summary>
    SecretProviderType ProviderType { get; }

    /// <summary>
    /// Indicates whether this provider supports dynamic refresh.
    /// Environment-only providers return false.
    /// </summary>
    bool SupportsRefresh { get; }

    /// <summary>
    /// Initializes the provider, authenticating with the secret manager.
    /// Must be called before retrieving secrets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SecretProviderException">When initialization fails.</exception>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a secret value by key.
    /// </summary>
    /// <param name="key">The canonical key (e.g., "Database:ConnectionString").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret value, or null if not found.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a secret with full metadata.
    /// </summary>
    /// <param name="key">The canonical key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret value with metadata, or null if not found.</returns>
    Task<SecretValue?> GetSecretWithMetadataAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all secrets under a path prefix.
    /// </summary>
    /// <param name="pathPrefix">The path prefix (e.g., "Keycloak").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of key-value pairs under the path.</returns>
    Task<IReadOnlyDictionary<string, string>> GetSecretsByPathAsync(
        string pathPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes secrets from the external source.
    /// Only applicable when <see cref="SupportsRefresh"/> is true.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SecretProviderException">When refresh fails.</exception>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health information including last refresh time and error counts.</returns>
    Task<ProviderHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default);
}
