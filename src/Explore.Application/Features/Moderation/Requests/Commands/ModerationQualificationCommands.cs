using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Coordinates and expected revisions are intent; the native repository requalifies current authority.</summary>
public abstract record ModerationQualificationCommand : ICommand<ModerationQualificationResponse>
{
    public required Guid OperationId { get; init; }
    public required AuthorityScope Target { get; init; }
    public required long ExpectedQualificationRevision { get; init; }

    internal abstract ModerationQualificationMutation ToMutation();
    public sealed override string ToString() => nameof(ModerationQualificationCommand);
}

public sealed record OpenModerationCaseCommand : ModerationQualificationCommand
{
    public required Guid CaseId { get; init; }
    public required ModerationSubjectKind SubjectKind { get; init; }
    public required Guid SubjectId { get; init; }
    public required Guid OccurrenceId { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new OpenModerationCaseSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        CaseId = CaseId,
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        OccurrenceId = OccurrenceId,
    };
}

public sealed record RecordModerationCaseResponseCommand : ModerationQualificationCommand
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Statement { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationCaseResponse
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        EvidenceId = EvidenceId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        Statement = Statement,
    };
}

public sealed record AssignModerationReviewerCommand : ModerationQualificationCommand
{
    public required Guid AssignmentId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReviewerHumanId { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AssignModerationReviewerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AssignmentId = AssignmentId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        ReviewerHumanId = ReviewerHumanId,
    };
}

public sealed record QualifyModerationReviewerControllerCommand : ModerationQualificationCommand
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required Guid ControllerHumanId { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset QualifiedUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new QualifyModerationReviewerController
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AssignmentId = AssignmentId,
        ExpectedAssignmentRevision = ExpectedAssignmentRevision,
        ControllerHumanId = ControllerHumanId,
        EvidenceId = EvidenceId,
        Justification = Justification,
        QualifiedUntil = QualifiedUntil,
    };
}

public sealed record AssessModerationReviewerConflictCommand : ModerationQualificationCommand
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required ModerationReviewerConflictState State { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AssessModerationReviewerConflict
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AssignmentId = AssignmentId,
        ExpectedAssignmentRevision = ExpectedAssignmentRevision,
        State = State,
        EvidenceId = EvidenceId,
        Justification = Justification,
    };
}

public sealed record WithdrawModerationReviewerCommand : ModerationQualificationCommand
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationReviewerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AssignmentId = AssignmentId,
        ExpectedAssignmentRevision = ExpectedAssignmentRevision,
    };
}

public sealed record QualifyModerationCaseUrgencyCommand : ModerationQualificationCommand
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new QualifyModerationCaseUrgency
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        EvidenceId = EvidenceId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        Justification = Justification,
        ValidUntil = ValidUntil,
    };
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record FinalizeModerationFindingCommand : ModerationQualificationCommand
{
    public required Guid FindingId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required ModerationRuleReference Rule { get; init; }
    public required ModerationRuleReference Reason { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new FinalizeModerationFindingSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        FindingId = FindingId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        PolicyId = PolicyId,
        ExpectedPolicyRevision = ExpectedPolicyRevision,
        Rule = Rule,
        Reason = Reason,
        EvidenceId = EvidenceId,
        Justification = Justification,
    };
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseModerationFindingCommand : ModerationQualificationCommand
{
    public required Guid FindingId { get; init; }
    public required long ExpectedFindingRevision { get; init; }
    public required Guid ReversalReferenceId { get; init; }
    public required string Justification { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new ReverseModerationFindingSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        FindingId = FindingId,
        ExpectedFindingRevision = ExpectedFindingRevision,
        ReversalReferenceId = ReversalReferenceId,
        Justification = Justification,
    };
}
