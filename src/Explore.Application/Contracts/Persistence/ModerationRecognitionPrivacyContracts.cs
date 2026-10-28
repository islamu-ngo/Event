using System.Collections.Immutable;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RecordModerationRecognitionPrivacySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationRecognitionPrivacyTerms Terms { get; init; }
    public required ModerationRecognitionPrivacyOwnership Ownership { get; init; }
    public required ImmutableArray<ModerationRecognitionPrivacyRetentionTerms> Retention { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationRecognitionPrivacySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationRecognitionPrivacyAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

public sealed record ModerationRecognitionPrivacySourceObservation(
    Guid TransactionId, ModerationRecognitionPrivacySource Source, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationRecognitionPrivacyAdoptionObservation(
    Guid TransactionId, ModerationRecognitionPrivacyAdoption Adoption, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
