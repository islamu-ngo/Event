using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationMeasuredOperationsCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required Guid ReviewerHumanId { get; init; }
    public required Guid ReviewerControllerHumanId { get; init; }
    public required Guid RecorderControllerHumanId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationMeasuredOperationsSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, ProfileId = ProfileId, Binding = Binding, ReviewerHumanId = ReviewerHumanId,
        ReviewerControllerHumanId = ReviewerControllerHumanId, RecorderControllerHumanId = RecorderControllerHumanId,
        ValidUntil = ValidUntil, OriginalEffect = OriginalEffect
    };
}

public sealed record AcceptModerationMeasuredOperationsCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AcceptanceId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AcceptModerationMeasuredOperationsSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration, AcceptanceId = AcceptanceId, ValidUntil = ValidUntil,
        OriginalEffect = OriginalEffect
    };
}

public sealed record WithdrawModerationMeasuredOperationsCommand : ModerationQualificationCommand
{
    public required Guid AcceptanceId { get; init; }
    public required long ExpectedAcceptanceRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationMeasuredOperationsAcceptance
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        AcceptanceId = AcceptanceId, ExpectedAcceptanceRevision = ExpectedAcceptanceRevision
    };
}
