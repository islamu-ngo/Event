using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Names retained assessment coordinates for correction, never current authority or collection permission.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawModerationProfileCollectionAssessmentCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required AuthorityScope Jurisdiction { get; init; }
    public required Guid SubjectHumanId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long PolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionGeneration { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }

    internal WithdrawModerationProfileCollectionAssessment ToWithdrawal() => new(
        Jurisdiction, SubjectHumanId, PolicyId, PolicyRevision, PublicationId, ProfileId,
        Mode, SourceId, ExpectedSourceGeneration, AdoptionId,
        ExpectedAdoptionGeneration, ExpectedAdoptionRevision);
}
