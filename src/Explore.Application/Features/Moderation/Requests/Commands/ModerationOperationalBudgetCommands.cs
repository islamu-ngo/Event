using System.Text.Json.Serialization;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationOperationalBudgetCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required ModerationOperationalBudgetTerms Terms { get; init; }
    public required Guid OperatorHumanId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new RecordModerationOperationalBudgetSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, Terms = Terms, OperatorHumanId = OperatorHumanId, ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationOperationalBudgetCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationOperationalBudgetSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration, AdoptionId = AdoptionId, ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawModerationOperationalBudgetCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationOperationalBudgetAdoption
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId, ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}
