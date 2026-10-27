using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public enum ModerationResourceSafetyOutcome { Unavailable, Known, Conflict, Denied }

/// <summary>Closed intent only. Tenant, human, controller and current native authority never come from this request.</summary>
public sealed record ModerationResourceSafetyReviewIntent
{
    public required Guid OperationId { get; init; }
    public required Guid QuarantineId { get; init; }
    public required Guid ResourceId { get; init; }
    public required Guid MaterialVersionId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReviewerAssignmentId { get; init; }
    public required long ExpectedReviewerRevision { get; init; }
    public required ResourceSafetyReviewStep Step { get; init; }
    public Guid? PredecessorId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    /// <summary>Purpose proposal only; subsequent steps inherit the independently adopted terms.</summary>
    public int RequiredApprovals { get; init; }
    public TimeSpan MaximumEvidenceLifetime { get; init; }
    public ResourceSafetyAttestation? Attestation { get; init; }
    public override string ToString() => nameof(ModerationResourceSafetyReviewIntent);
}

public sealed record ModerationResourceQuarantineReleaseIntent(
    Guid OperationId, Guid QuarantineId, Guid ResourceId, Guid MaterialVersionId, Guid ApprovalId)
{
    public override string ToString() => nameof(ModerationResourceQuarantineReleaseIntent);
}

public sealed record ModerationResourceSafetyReviewResult(
    ModerationResourceSafetyOutcome Outcome, EventResourceModerationQuarantineReview? Receipt = null);
public sealed record ModerationResourceQuarantineReleaseResult(
    ModerationResourceSafetyOutcome Outcome, EventResourceModerationQuarantineRelease? Receipt = null);

public interface IModerationResourceQuarantineReleaseRepository
{
    Task<ModerationResourceSafetyReviewResult> ReviewAsync(
        ModerationResourceSafetyReviewIntent intent, CancellationToken cancellationToken = default);
    Task<ModerationResourceQuarantineReleaseResult> ReleaseAsync(
        ModerationResourceQuarantineReleaseIntent intent, CancellationToken cancellationToken = default);
}
