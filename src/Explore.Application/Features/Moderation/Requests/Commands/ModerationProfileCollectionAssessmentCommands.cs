using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Expected original coordinates only; the native owner resolves the subject, actor and current parents.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationProfileCollectionAssessmentOriginalIntent
{
    [JsonRequired] public required Guid ObservationId { get; init; }
    [JsonRequired] public required long ExpectedObservationRevision { get; init; }
    [JsonRequired] public required long ExpectedObservationGeneration { get; init; }
    [JsonRequired] public required AuthorityScope Jurisdiction { get; init; }
    [JsonRequired] public required Guid PolicyId { get; init; }
    [JsonRequired] public required long PolicyRevision { get; init; }
    [JsonRequired] public required Guid PublicationId { get; init; }
    [JsonRequired] public required Guid ProfileId { get; init; }
    [JsonRequired] public required ModerationNativeMode Mode { get; init; }
    [JsonRequired] public required Guid ProcedureSourceId { get; init; }
    [JsonRequired] public required long ExpectedProcedureSourceRevision { get; init; }
    [JsonRequired] public required long ExpectedProcedureSourceWriterGeneration { get; init; }
    [JsonRequired] public required Guid ProcedureAdoptionId { get; init; }
    [JsonRequired] public required long ExpectedProcedureAdoptionRevision { get; init; }
    [JsonRequired] public required long ExpectedProcedureAdoptionWriterGeneration { get; init; }

    public ModerationProfileCollectionAssessmentOriginal ToOriginal() => new(
        ObservationId, ExpectedObservationRevision, ExpectedObservationGeneration,
        Jurisdiction, PolicyId, PolicyRevision, PublicationId, ProfileId, Mode,
        ProcedureSourceId, ExpectedProcedureSourceRevision, ExpectedProcedureSourceWriterGeneration,
        ProcedureAdoptionId, ExpectedProcedureAdoptionRevision, ExpectedProcedureAdoptionWriterGeneration);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationProfileCollectionAssessmentCommand : ICommand<BaseCommandResponse<Guid>>
{
    private readonly IReadOnlyList<ModerationCollectionPrerequisiteKind> required = null!;

    [JsonRequired] public required ModerationProfileCollectionAssessmentOriginalIntent Original { get; init; }
    [JsonRequired] public required Guid SourceId { get; init; }
    [JsonRequired] public required long ExpectedGeneration { get; init; }
    [JsonRequired]
    public required IReadOnlyList<ModerationCollectionPrerequisiteKind> Required
    {
        get => required;
        init => required = value is null ? null! : Array.AsReadOnly(value.Take(4).ToArray());
    }
    [JsonRequired] public required DateTimeOffset ValidUntil { get; init; }

    public RecordModerationProfileCollectionAssessment ToRecord() => new()
    {
        Original = Original.ToOriginal(), SourceId = SourceId, ExpectedGeneration = ExpectedGeneration,
        Required = Required, ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationProfileCollectionAssessmentCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonRequired] public required ModerationProfileCollectionAssessmentOriginalIntent Original { get; init; }
    [JsonRequired] public required Guid SourceId { get; init; }
    [JsonRequired] public required long ExpectedSourceRevision { get; init; }
    [JsonRequired] public required long ExpectedSourceGeneration { get; init; }
    [JsonRequired] public required long ExpectedGeneration { get; init; }
    [JsonRequired] public required Guid AdoptionId { get; init; }
    [JsonRequired] public required DateTimeOffset ValidUntil { get; init; }

    public AdoptModerationProfileCollectionAssessment ToAdoption() => new()
    {
        Original = Original.ToOriginal(), SourceId = SourceId, ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceGeneration = ExpectedSourceGeneration, ExpectedGeneration = ExpectedGeneration,
        AdoptionId = AdoptionId, ValidUntil = ValidUntil
    };
}
