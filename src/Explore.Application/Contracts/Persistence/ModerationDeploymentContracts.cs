using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Intent to observe a selected mode; runtime and primary owners must independently establish its coverage.</summary>
public sealed record RecordModerationDeploymentSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationDeploymentSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationDeploymentAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

/// <summary>The actual current retained source, after reacquiring its complete native deployment coverage.</summary>
public sealed record ModerationDeploymentSourceObservation(
    Guid TransactionId, ModerationDeploymentSource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

/// <summary>Separately qualified deployment adoption, not approval of other publication obligations.</summary>
public sealed record ModerationDeploymentAdoptionObservation(
    Guid TransactionId, ModerationDeploymentAdoption Adoption, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
