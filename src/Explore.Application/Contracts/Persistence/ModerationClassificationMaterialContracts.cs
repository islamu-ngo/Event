using System.Collections.Immutable;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationClassificationMaterialSourceOwner
{
    Task<ModerationClassificationMaterialInventoryObservation?> AcquireInventoryAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid eventGenerationId,
        Guid taxonomyId, Guid profileId, ModerationNativeMode mode, ModerationClassificationMaterialMandate mandate,
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task<ModerationClassificationMaterialObservation?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid eventId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken cancellationToken);
}

public sealed record RecordModerationClassificationMaterialSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedMaterialGeneration { get; init; }
    public required Guid EventGenerationId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required Guid TaxonomyId { get; init; }
    public required Guid ExpectedTaxonomySourceId { get; init; }
    public required Guid ExpectedTaxonomyAdoptionId { get; init; }
    public required ModerationClassificationMaterialMandate Mandate { get; init; }
    public required ImmutableArray<ModerationClassificationMaterialSelection> Materials { get; init; }
    public required ImmutableArray<ModerationClassificationMaterialComponentVersion> Components { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AcceptModerationClassificationMaterialSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid ReviewId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record EndModerationClassificationMaterialReview : ModerationQualificationMutation
{
    public required Guid ReviewId { get; init; }
    public required long ExpectedReviewRevision { get; init; }
    public required ModerationClassificationMaterialEnd End { get; init; }
    public required string Grounds { get; init; }
}

/// <summary>Current native version coordinates to review, never client-owned completeness or permission.</summary>
public sealed record ModerationClassificationMaterialInventoryObservation(
    Guid TransactionId, ModerationEventGeneration Generation, ModerationClassificationTaxonomyObservation Taxonomy,
    ModerationChildAgePolicySource AgeSource, ModerationChildAgePolicyAdoption AgeAdoption,
    ImmutableArray<ModerationClassificationMaterial> Materials,
    ImmutableArray<ModerationClassificationMaterialComponentVersion> Components,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

/// <summary>
/// Complete independently reviewed metadata for the existing pure audience compiler.
/// Reacquire on the original native transaction at final use. This is not an audience grant,
/// age assertion, collection receipt, conduct finding or permission to expose payloads.
/// </summary>
public sealed record ModerationClassificationMaterialObservation(
    Guid TransactionId, ModerationClassificationMaterialSource Source, ModerationClassificationMaterialReview Acceptance,
    ModerationEventGeneration Generation, ModerationClassificationTaxonomyObservation Taxonomy,
    ModerationChildAgePolicySource AgeSource, ModerationChildAgePolicyAdoption AgeAdoption,
    ImmutableArray<EventContentClassification> Reviews, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
