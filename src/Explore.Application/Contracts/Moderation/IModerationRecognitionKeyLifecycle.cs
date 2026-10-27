using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Raw, independently refreshed selected-secret observation for lifecycle operations only.
/// Recognition consumers must use IModerationRecognitionKeyRingProvider, which also checks
/// durable lifecycle authorization. Neither interface writes external secret material.
/// </summary>
public interface IModerationRecognitionKeyMaterialSource
{
    Task<ModerationRecognitionKeyRing> ObserveAsync(Guid trustDomainId, CancellationToken cancellationToken);
}

public interface IModerationRecognitionKeyLifecycleGuard
{
    /// <summary>
    /// Requires the original native transaction and identity fence at terminal capture boundaries.
    /// A returned snapshot never replaces the caller's final transaction revalidation.
    /// </summary>
    Task ValidateAsync(ModerationRecognitionKeyRing ring, CancellationToken cancellationToken);
}

/// <summary>
/// Exact metadata/selected-byte join. Ring JSON remains v1; candidate means readable during overlap.
/// Retired and compromised material is never accepted by the recognition provider.
/// </summary>
public static class ModerationRecognitionKeyLifecycleSnapshot
{
    public static void RequireMatching(ModerationRecognitionKeyLifecycle lifecycle,
        ModerationRecognitionKeyRing ring, DateTimeOffset now)
    {
        lifecycle.ValidatePersistedState();
        var expected = lifecycle.Keys.Where(row => row.State != ModerationRecognitionKeyState.Retired).ToArray();
        if (ring.TrustDomainId != lifecycle.Id || ring.RingGeneration != lifecycle.RingGeneration
            || now < ring.ObservedAt || now >= ring.CurrentUntil || now < lifecycle.ChangedAt
            || expected.Length != ring.Keys.Length
            || expected.Any(row => row.State == ModerationRecognitionKeyState.Compromised
                || !ring.Keys.Any(actual => actual.KeyId == row.KeyId && actual.KeyGeneration == row.KeyGeneration
                    && actual.VerificationTag == row.VerificationTag
                    && actual.IsWriting == (row.State == ModerationRecognitionKeyState.Writing))))
            throw new InvalidOperationException("moderation_recognition_lifecycle_unavailable");
    }
}
