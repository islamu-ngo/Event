using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RecordModerationChildAgePolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationChildAgePolicyTerms Terms { get; init; }
    public required ModerationChildAgePolicyOwnership Ownership { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationChildAgePolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationChildAgePolicyAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

public sealed record ModerationChildAgePolicySourceObservation(
    Guid TransactionId, ModerationChildAgePolicySource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationChildAgePolicyAdoptionObservation(
    Guid TransactionId, ModerationChildAgePolicyAdoption Adoption, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

/// <summary>
/// Independently adopted calendar semantics on one live native acquisition.
/// Source/adoption coordinates are provenance, not individual assurance, collection permission
/// or activation. Consumers must retain native fences and reacquire at their own checkpoints.
/// </summary>
public sealed record ModerationChildAgeCalendarObservation(
    Guid TransactionId, ModerationAgePolicy Policy,
    Guid SourceId, long SourceRevision, long SourceWriterGeneration,
    Guid AdoptionId, long AdoptionRevision, long AdoptionWriterGeneration,
    int AdulthoodAge, int DebateMinimumAge, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
