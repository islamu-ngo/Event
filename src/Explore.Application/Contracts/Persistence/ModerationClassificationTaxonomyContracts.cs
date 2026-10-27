using System.Collections.Immutable;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationClassificationTaxonomySourceOwner
{
    Task<ModerationClassificationTaxonomyObservation?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid taxonomyId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken cancellationToken);
}

public sealed record RecordModerationClassificationTaxonomySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid TaxonomyId { get; init; }
    public required long ExpectedTaxonomyGeneration { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationClassificationTaxonomyMandate Mandate { get; init; }
    public required ImmutableArray<ModerationClassificationTaxonomyCategoryTerms> Categories { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationClassificationTaxonomySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationClassificationTaxonomyAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

/// <summary>
/// Finite independently accepted category metadata on an original live native transaction.
/// Not event material approval, individual collection permission, age assurance or an audience grant.
/// Consumers must reacquire this observation at their final native checkpoint.
/// </summary>
public sealed record ModerationClassificationTaxonomyObservation(
    Guid TransactionId, ModerationClassificationTaxonomySource Source, ModerationClassificationTaxonomyAdoption Adoption,
    ImmutableArray<EventContentClassificationVersion> Categories, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
