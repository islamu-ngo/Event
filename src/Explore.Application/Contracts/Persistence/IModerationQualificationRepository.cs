using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationQualificationRepository
{
    /// <summary>
    /// Requalifies finite native control-plane authority, mutates the actual source and
    /// its epoch atomically, and retains an exact identity/request-bound receipt.
    /// </summary>
    Task<ModerationQualificationWriteResult> ExecuteAsync(
        ModerationQualificationMutation mutation, CancellationToken cancellationToken = default);
}

public enum ModerationQualificationWriteOutcome
{
    Known = 1,
    Unauthenticated = 2,
    Denied = 3,
    Conflict = 4,
    NotFound = 5,
    Unavailable = 6
}

public sealed record ModerationQualificationWriteResult(
    ModerationQualificationWriteOutcome Outcome,
    ModerationQualificationMutationReceipt? Receipt = null);

/// <summary>Target and expected generations are intent, never caller or jurisdiction proof.</summary>
public abstract record ModerationQualificationMutation
{
    public required Guid OperationId { get; init; }
    public required AuthorityScope Target { get; init; }
    public required long ExpectedQualificationRevision { get; init; }
    public sealed override string ToString() => nameof(ModerationQualificationMutation);
}

public sealed record OpenModerationCaseSource : ModerationQualificationMutation
{
    public required Guid CaseId { get; init; }
    public required ModerationSubjectKind SubjectKind { get; init; }
    public required Guid SubjectId { get; init; }
    public required Guid OccurrenceId { get; init; }
}

public sealed record RecordModerationCaseResponse : ModerationQualificationMutation
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Statement { get; init; }
}

public sealed record AssignModerationReviewerSource : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReviewerHumanId { get; init; }
}

public sealed record QualifyModerationReviewerController : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required Guid ControllerHumanId { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset QualifiedUntil { get; init; }
}

public sealed record AssessModerationReviewerConflict : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required ModerationReviewerConflictState State { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
}

public sealed record WithdrawModerationReviewerSource : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
}

public sealed record QualifyModerationCaseUrgency : ModerationQualificationMutation
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record FinalizeModerationFindingSource : ModerationQualificationMutation
{
    public required Guid FindingId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required ModerationRuleReference Rule { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
}

public sealed record ReverseModerationFindingSource : ModerationQualificationMutation
{
    public required Guid FindingId { get; init; }
    public required long ExpectedFindingRevision { get; init; }
    public required Guid ReversalReferenceId { get; init; }
    public required string Justification { get; init; }
}

/// <summary>Authorable meaning only. Native current effect evidence cannot be imported through this contract.</summary>
public sealed record ModerationPolicyRuleDraft
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required long Revision { get; init; }
    public required string Meaning { get; init; }
    public required IReadOnlyList<ModerationPolicyOperationPurpose> Purposes { get; init; }
    public required IReadOnlyList<ModerationCapability> Capabilities { get; init; }
    public required TimeSpan MaximumFiniteDuration { get; init; }
    public required bool AllowsIndefinite { get; init; }
    public required TimeSpan MaximumReviewDelay { get; init; }
    public required int MinimumApprovals { get; init; }
    public required int MaximumApprovals { get; init; }
}

public sealed record AdoptModerationPolicySource : ModerationQualificationMutation
{
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required IReadOnlyList<ModerationPolicyRuleDraft> Rules { get; init; }
}
