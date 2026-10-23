using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record AdoptModerationProfileCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationProfileSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        SelectionId = SelectionId,
        Mode = Mode,
        CaseId = CaseId,
        ExpectedCaseRevision = ExpectedCaseRevision,
        ValidUntil = ValidUntil
    };
}

public sealed record RevokeModerationProfileCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationProfileSource
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
