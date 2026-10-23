using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationClassificationControlSelectionCommand : ModerationQualificationCommand
{
    public required Guid SelectionId { get; init; }
    public required Guid MaterialSourceId { get; init; }
    public required long ExpectedMaterialSourceRevision { get; init; }
    public required long ExpectedMaterialWriterGeneration { get; init; }
    public required Guid MaterialReviewId { get; init; }
    public required long ExpectedMaterialReviewRevision { get; init; }
    public required long ExpectedReviewWriterGeneration { get; init; }
    public required Guid EventGenerationId { get; init; }
    public required long ExpectedEventGenerationRevision { get; init; }
    public required long ExpectedEventWriterGeneration { get; init; }
    public required ModerationPolicyContribution Contribution { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationClassificationControlSelection
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SelectionId = SelectionId,
        MaterialSourceId = MaterialSourceId,
        ExpectedMaterialSourceRevision = ExpectedMaterialSourceRevision,
        ExpectedMaterialWriterGeneration = ExpectedMaterialWriterGeneration,
        MaterialReviewId = MaterialReviewId,
        ExpectedMaterialReviewRevision = ExpectedMaterialReviewRevision,
        ExpectedReviewWriterGeneration = ExpectedReviewWriterGeneration,
        EventGenerationId = EventGenerationId,
        ExpectedEventGenerationRevision = ExpectedEventGenerationRevision,
        ExpectedEventWriterGeneration = ExpectedEventWriterGeneration,
        Contribution = Contribution,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationClassificationEventTargetCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required long ExpectedSelectionRevision { get; init; }
    public required int BindingOrdinal { get; init; }
    public required Guid EventGenerationId { get; init; }
    public required long ExpectedEventGenerationRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationClassificationEventTarget
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        SelectionId = SelectionId,
        ExpectedSelectionRevision = ExpectedSelectionRevision,
        BindingOrdinal = BindingOrdinal,
        EventGenerationId = EventGenerationId,
        ExpectedEventGenerationRevision = ExpectedEventGenerationRevision,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RevokeModerationClassificationEventTargetCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required string Grounds { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationClassificationEventTarget
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision,
        Grounds = Grounds
    };
}
