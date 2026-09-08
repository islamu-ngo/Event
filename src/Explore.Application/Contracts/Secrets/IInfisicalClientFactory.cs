namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Returns an authenticated <see cref="IInfisicalClient"/> for secret retrieval. Implementations are
/// expected to cache the authenticated client and renew tokens transparently.
/// </summary>
public interface IInfisicalClientFactory
{
    /// <summary>
    /// Returns a ready-to-use client. Returns <c>null</c> when the Infisical integration is not
    /// configured (missing client id/secret/site URL) so the caller can surface a clean "not configured"
    /// error rather than an authentication failure. Provider failures are thrown for typed translation.
    /// </summary>
    Task<IInfisicalClient?> GetClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Library-agnostic façade over an Infisical SDK client. Only exposes the operations needed by
/// <c>InfisicalSecretSource</c>.
/// </summary>
public interface IInfisicalClient
{
    /// <summary>
    /// Reads a single secret at the given environment + folder path + name. Returns the raw plaintext
    /// value, or <c>null</c> when the secret is absent. Transient errors should surface as exceptions
    /// so the <c>InfisicalSecretSource</c> can log and translate them to a <c>null</c> result.
    /// </summary>
    Task<string?> GetSecretAsync(
        string environment,
        string folderPath,
        string secretName,
        CancellationToken cancellationToken = default);

    Task<bool> WriteSecretAsync(
        string environment,
        string folderPath,
        string secretName,
        ReadOnlyMemory<byte> secretValue,
        CancellationToken cancellationToken = default);
}
