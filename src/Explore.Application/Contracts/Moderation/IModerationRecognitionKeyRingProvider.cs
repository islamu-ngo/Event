namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Acquires an owned, finite snapshot from the existing explicitly selected secret authority.
/// A successful acquisition is not capture, activation, convergence or retirement permission.
/// </summary>
public interface IModerationRecognitionKeyRingProvider
{
    /// <summary>
    /// The caller supplies its independently qualified trust domain and disposes the returned
    /// snapshot and every borrowed key. Failure closes affected recognition; no key is invented.
    /// </summary>
    Task<ModerationRecognitionKeyRing> ResolveAsync(Guid trustDomainId, CancellationToken cancellationToken);
}
