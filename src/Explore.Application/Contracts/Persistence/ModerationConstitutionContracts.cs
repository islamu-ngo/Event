using System.Collections.Immutable;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RecordModerationConstitutionSource : ModerationQualificationMutation
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationConstitutionTerms Terms { get; init; }
    public required ImmutableArray<ModerationConstitutionReviewerTerms> Reviewers { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationConstitutionSource : ModerationQualificationMutation
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationConstitutionAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

public sealed record RecordModerationConstitutionAvailability : ModerationQualificationMutation
{
    public required Guid AvailabilityId { get; init; }
    public required int MaximumCaseload { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationConstitutionAvailability : ModerationQualificationMutation
{
    public required Guid AvailabilityId { get; init; }
    public required long ExpectedAvailabilityRevision { get; init; }
}

public sealed record ModerationConstitutionSourceObservation(
    Guid TransactionId, ModerationConstitutionSource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationConstitutionAdoptionObservation(
    Guid TransactionId, ModerationConstitutionAdoption Adoption, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
