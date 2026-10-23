using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationCaseNarrativePolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required ModerationCaseTargetKind TargetKind { get; init; }
    public required ModerationCaseNarrativeTerms Terms { get; init; }
    public required ModerationCaseNarrativeOwnership Ownership { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationCaseNarrativePolicySource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, TargetKind = TargetKind, Terms = Terms, Ownership = Ownership, ValidUntil = ValidUntil
    };
}

public sealed record AdoptModerationCaseNarrativePolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationCaseNarrativePolicySource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration, AdoptionId = AdoptionId, ValidUntil = ValidUntil
    };
}

public sealed record WithdrawModerationCaseNarrativePolicyCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationCaseNarrativeAdoption
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId, ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}
