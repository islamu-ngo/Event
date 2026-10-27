using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native maintenance ownership of committed effects. Requested identities and revisions are
/// intent, not authority. Claim recovers expired leases. Only a retained original inert capture
/// can finalize an exact lease here; substantive consequences and delivery require their own owners.
/// </summary>
public interface IModerationEffectLeaseRepository
{
    Task<ModerationEffectLeaseResult> ReadAsync(Guid effectId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Proves original inert meaning, including an already completed lease, without changing ownership.
    /// Finite disclosure-retention metadata rejects substantive originals before their private body
    /// is read; that custody belongs to the qualified consequence owner, not this probe.
    /// </summary>
    Task<ModerationEffectLeaseResult> ReadWithoutSubstantiveWorkAsync(
        Guid effectId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Acknowledges the native original owner's already committed completion transition through
    /// its host/writer-bound effect-ledger marker, including after original custody cleanup.
    /// It reads no private original, extends no retention and grants no new completion.
    /// </summary>
    Task<ModerationEffectLeaseResult> ReadOriginalCompletionAsync(
        Guid effectId, CancellationToken cancellationToken = default);
    Task<ModerationEffectLeaseResult> ClaimAsync(
        Guid effectId, long expectedRevision, CancellationToken cancellationToken = default);
    Task<ModerationEffectLeaseResult> ReleaseAsync(
        Guid effectId, Guid leaseId, long expectedRevision, CancellationToken cancellationToken = default);
    /// <summary>
    /// Finalizes only an originally inert effect under its current exact unexpired lease.
    /// The repository proves the saved original capture; callers supply no completion evidence.
    /// A repeated external invocation conflicts; ambiguous acknowledgement replay is invocation-local.
    /// </summary>
    Task<ModerationEffectLeaseResult> CompleteWithoutSubstantiveWorkAsync(
        Guid effectId, Guid leaseId, long expectedRevision, CancellationToken cancellationToken = default);
}

public enum ModerationEffectLeaseOutcome
{
    Unavailable,
    Known,
    Busy,
    Completed,
    Conflict,
    Denied,
    NotFound
}

public enum ModerationEffectLeaseUnavailableReason
{
    NativeAuthority,
    MissingCause,
    InconsistentCause,
    LeaseWindowElapsed,
    MissingOriginalCapture,
    InvalidOrSubstantiveOriginalCapture
}

/// <summary>Returns the native entity only after authority and canonical tenancy have been reread.</summary>
public sealed record ModerationEffectLeaseResult(
    ModerationEffectLeaseOutcome Outcome,
    ModerationEffectIntent? Effect = null,
    ModerationEffectLeaseUnavailableReason? UnavailableReason = null);
