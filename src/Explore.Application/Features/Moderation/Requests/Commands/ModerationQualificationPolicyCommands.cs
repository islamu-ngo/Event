using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record PublishModerationQualificationPolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required TimeSpan MaximumReviewerLifetime { get; init; }
    public required TimeSpan MaximumUrgencyLifetime { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new PublishModerationQualificationPolicySource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        ExpectedSourceRevision = ExpectedSourceRevision,
        MaximumReviewerLifetime = MaximumReviewerLifetime,
        MaximumUrgencyLifetime = MaximumUrgencyLifetime,
        ValidUntil = ValidUntil
    };
}

public sealed record AdoptModerationQualificationPolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationQualificationPolicySource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedWriterGeneration = ExpectedWriterGeneration,
        AdoptionId = AdoptionId,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        ValidUntil = ValidUntil
    };
}

public sealed record RevokeModerationQualificationPolicyAdoptionCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationQualificationPolicyAdoption
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision
    };
}
