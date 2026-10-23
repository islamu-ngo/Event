using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record AdoptModerationEventTargetCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required int BindingOrdinal { get; init; }
    public required Guid GenerationId { get; init; }
    public required long ExpectedGenerationRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationEventTargetSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        SelectionId = SelectionId,
        BindingOrdinal = BindingOrdinal,
        GenerationId = GenerationId,
        ExpectedGenerationRevision = ExpectedGenerationRevision,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        ValidUntil = ValidUntil
    };
}

public sealed record RevokeModerationEventTargetCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationEventTargetSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision
    };
}
