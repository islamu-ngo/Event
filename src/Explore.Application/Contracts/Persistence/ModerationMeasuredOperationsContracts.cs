using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>The native writer acquires evidence; callers cannot submit measurements or a verdict.</summary>
public sealed record RecordModerationMeasuredOperationsSource : ModerationQualificationMutation
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required Guid ReviewerHumanId { get; init; }
    public required Guid ReviewerControllerHumanId { get; init; }
    public required Guid RecorderControllerHumanId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AcceptModerationMeasuredOperationsSource : ModerationQualificationMutation
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AcceptanceId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationMeasuredOperationsAcceptance : ModerationQualificationMutation
{
    public required Guid AcceptanceId { get; init; }
    public required long ExpectedAcceptanceRevision { get; init; }
}

public sealed record ModerationMeasuredOperationsSourceObservation(
    Guid TransactionId, ModerationMeasuredOperationsSource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationMeasuredOperationsAcceptanceObservation(
    Guid TransactionId, ModerationMeasuredOperationsAcceptance Acceptance, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
