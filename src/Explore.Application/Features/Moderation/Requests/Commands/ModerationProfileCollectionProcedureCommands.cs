using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Proposes typed procedure metadata; native owners independently authenticate parents and collection authority.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationProfileCollectionProcedureCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid ChildAgeSourceId { get; init; }
    public required long ExpectedChildAgeSourceRevision { get; init; }
    public required long ExpectedChildAgeSourceWriterGeneration { get; init; }
    public required Guid ChildAgeAdoptionId { get; init; }
    public required long ExpectedChildAgeAdoptionRevision { get; init; }
    public required long ExpectedChildAgeAdoptionWriterGeneration { get; init; }
    public required ModerationProfileCollectionProcedure Procedure { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationProfileCollectionProcedureSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ChildAgeSourceId = ChildAgeSourceId,
        ExpectedChildAgeSourceRevision = ExpectedChildAgeSourceRevision,
        ExpectedChildAgeSourceWriterGeneration = ExpectedChildAgeSourceWriterGeneration,
        ChildAgeAdoptionId = ChildAgeAdoptionId,
        ExpectedChildAgeAdoptionRevision = ExpectedChildAgeAdoptionRevision,
        ExpectedChildAgeAdoptionWriterGeneration = ExpectedChildAgeAdoptionWriterGeneration,
        Procedure = Procedure,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationProfileCollectionProcedureCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationProfileCollectionProcedureSource
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
public sealed record WithdrawModerationProfileCollectionProcedureCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationProfileCollectionProcedureAdoption
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}
