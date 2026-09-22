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
    /// Reads one secret plus its provider-owned id/version revision. The
    /// revision must be value-free and change when the provider secret
    /// changes. Returns <c>null</c> when the secret is absent.
    /// </summary>
    Task<SecretProviderValue?> GetSecretAsync(
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

/// <summary>Plaintext provider value paired with value-free version metadata.</summary>
public sealed record SecretProviderValue(
    string Value,
    string Revision);
