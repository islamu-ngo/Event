using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationClassificationMaterialCommand : ModerationQualificationCommand
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

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationClassificationMaterialSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedMaterialGeneration = ExpectedMaterialGeneration,
        EventGenerationId = EventGenerationId,
        PolicyId = PolicyId,
        ExpectedPolicyRevision = ExpectedPolicyRevision,
        PublicationId = PublicationId,
        ProfileId = ProfileId,
        Mode = Mode,
        TaxonomyId = TaxonomyId,
        ExpectedTaxonomySourceId = ExpectedTaxonomySourceId,
        ExpectedTaxonomyAdoptionId = ExpectedTaxonomyAdoptionId,
        Mandate = Mandate,
        Materials = Materials,
        Components = Components,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptModerationClassificationMaterialCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid ReviewId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AcceptModerationClassificationMaterialSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration,
        ReviewId = ReviewId,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EndModerationClassificationMaterialCommand : ModerationQualificationCommand
{
    public required Guid ReviewId { get; init; }
    public required long ExpectedReviewRevision { get; init; }
    public required ModerationClassificationMaterialEnd End { get; init; }
    public required string Grounds { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new EndModerationClassificationMaterialReview
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        ReviewId = ReviewId,
        ExpectedReviewRevision = ExpectedReviewRevision,
        End = End,
        Grounds = Grounds
    };
}
