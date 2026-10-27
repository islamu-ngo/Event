using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads current configured-key metadata without exposing credential material or falling back to persisted keys.</summary>
public interface IConfiguredApiKeyResourceAuthorityReader
{
    ValueTask<ApiKeyPrincipalContext?> ReadAsync(
        string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Acquires the original configured source for one terminal owner. Metadata is not action
    /// permission; the owner must retain this lifetime and its native authority through completion.
    /// </summary>
    ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
        string keyId, CancellationToken cancellationToken);

    /// <summary>
    /// Retains the source actually used for this request's successful configured-key authentication.
    /// Missing origin, caller mismatch or replacement is unavailable; metadata alone is insufficient.
    /// </summary>
    ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
        string keyId, CancellationToken cancellationToken);
}

/// <summary>
/// Retains one original named source and its actual credential expiry. Replacement, expiry or
/// disposal invalidates the lifetime; a recheck never adopts a replacement source or renews it.
/// </summary>
public interface IConfiguredApiKeyResourceAuthorityLease : IAsyncDisposable
{
    ApiKeyPrincipalContext Principal { get; }
    DateTimeOffset? ExpiresAtUtc { get; }
    CancellationToken ValidityCancellation { get; }
    ValueTask<bool> RevalidateAsync(CancellationToken cancellationToken);
}
