using Explore.Application.Contracts.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native material-free lifecycle owner. Mutation callers are trusted maintenance operations,
/// not public request bodies. Expected revisions reject duplicate or racing operator intent.
/// All writes retain the selected identity gate, primary transaction and lifecycle slot.
/// </summary>
public interface IModerationRecognitionKeyLifecycleRepository : IModerationRecognitionKeyLifecycleGuard
{
    Task<ModerationRecognitionKeyLifecycle?> FindAsync(Guid trustDomainId, CancellationToken token);
    Task<ModerationRecognitionKeyLifecycle> InitializeAsync(ModerationRecognitionKeyRing observed,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token);
    Task<ModerationRecognitionKeyLifecycle> StageAsync(ModerationRecognitionKeyRing observed, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token);
    Task<KeyReplica> AcknowledgeAsync(ModerationRecognitionKeyRing independentlyObserved, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token);
    Task<ModerationRecognitionKeyLifecycle> SwitchAsync(ModerationRecognitionKeyRing observed, string keyId, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token);
    Task<ModerationRecognitionKeyLifecycle> RetireAsync(ModerationRecognitionKeyRing observed, string keyId, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token);
    Task<ModerationRecognitionKeyLifecycle> CompromiseAsync(Guid trustDomainId, string keyId, long expectedRevision, CancellationToken token);

    /// <summary>
    /// Call after Capture and before the original Save/receipt commit, and again after BindAuthority.
    /// Never opens or commits a different transaction. The original source, including an alias's
    /// original retention ceiling, establishes each finite obligation.
    /// </summary>
    Task EnlistCaptureAsync(ModerationRecognitionPending pending, CancellationToken token);

    /// <summary>
    /// Bounded whole-receipt cleanup, using a caller cursor rather than repeatedly visiting blockers.
    /// Deletes only expired eligible matching material and its expired operational references.
    /// Correction evidence, legal holds and disposition evidence are not owned by this method.
    /// </summary>
    Task<ModerationRecognitionKeyLifecycleCleanup> PruneAsync(
        Guid? afterIntentId, int limit, CancellationToken token);
    /// <summary>Diagnostic lookup only; an unqualified scalar receipt never authorizes replay.</summary>
    Task<ModerationRecognitionKeyLifecyclePurge?> FindPurgeAsync(Guid intentId, CancellationToken token);
    /// <summary>
    /// Qualifies the complete finite summary against its original obligations and independently
    /// reread selected-authority fact. Requires the caller's original identity/primary transaction;
    /// retain those fences and recheck RetainUntil at the final native commit checkpoint.
    /// </summary>
    Task<ModerationRecognitionKeyLifecyclePurge?> AcquirePurgeAsync(PrivacyErasureIntent fact, CancellationToken token);
    /// <summary>
    /// Removes only already-purged, expired privacy-linked receipts/obligations. Unlinkable key
    /// horizons and authority watermarks remain. This keyless primary-only maintenance operation
    /// never needs an available secret or erasure authority to prolong a privacy receipt ceiling.
    /// </summary>
    Task<ModerationRecognitionKeyLifecycleReceiptCleanup> ForgetExpiredReceiptsAsync(
        Guid? afterIntentId, int limit, CancellationToken token);
}
