using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationClassificationTaxonomyCommand : ModerationQualificationCommand
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

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationClassificationTaxonomySource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        TaxonomyId = TaxonomyId,
        ExpectedTaxonomyGeneration = ExpectedTaxonomyGeneration,
        PolicyId = PolicyId,
        ExpectedPolicyRevision = ExpectedPolicyRevision,
        PublicationId = PublicationId,
        ProfileId = ProfileId,
        Mode = Mode,
        Mandate = Mandate,
        Categories = Categories,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationClassificationTaxonomyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationClassificationTaxonomySource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration,
        AdoptionId = AdoptionId,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawModerationClassificationTaxonomyCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationClassificationTaxonomyAdoption
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}
