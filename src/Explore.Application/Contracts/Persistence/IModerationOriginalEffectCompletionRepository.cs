using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Completes only a retained original CFG09 recipe. Inputs select an exact native lease,
/// never supply proof. The owner reads all original obligations and native committed facts,
/// requalifies current consumer/purpose authority and owns the atomic final transition.
/// Expired custody stays pending; this port does not reconstruct historical private authority.
/// </summary>
public interface IModerationOriginalEffectCompletionRepository
{
    Task<ModerationOriginalEffectCompletionResult> CompleteAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, CancellationToken cancellationToken = default);
}

public enum ModerationOriginalEffectCompletionOutcome { Pending, Completed, Conflict }

public enum ModerationOriginalEffectCompletionPendingReason
{
    CurrentAuthority,
    OriginalCustody,
    OriginalRecipe,
    OriginalCauseAccounting,
    OriginalNotification,
    ProtectedContentRecourse,
    IndependentObjectReview,
    TerminalConsumerProof,
    NativePurposeProof,
    FinalCommitBoundary
}

public sealed record ModerationOriginalEffectCompletionResult(
    ModerationOriginalEffectCompletionOutcome Outcome,
    ModerationOriginalEffectCompletionReceipt? Receipt = null,
    ModerationOriginalEffectCompletionPendingReason? PendingReason = null);
