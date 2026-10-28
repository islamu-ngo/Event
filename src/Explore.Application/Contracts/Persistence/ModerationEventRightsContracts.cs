using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RecordModerationEventRightsSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationEventRightsTerms Terms { get; init; }
    public required ModerationEventRightsOwnership Ownership { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationEventRightsSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationEventRightsAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

/// <summary>Original-transaction normative source evidence, not a concrete event or participant mapping.</summary>
public sealed record ModerationEventRightsSourceObservation(
    Guid TransactionId, ModerationEventRightsSource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

/// <summary>Independent normative acceptance; actual permissions and availability remain with native consequence owners.</summary>
public sealed record ModerationEventRightsAdoptionObservation(
    Guid TransactionId, ModerationEventRightsAdoption Adoption, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
