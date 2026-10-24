using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Expected native acceptance coordinates only, never a subject, required set or authenticated prerequisite.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationProfileSubjectConsentAssessmentIntent
{
    [JsonRequired] public required ModerationProfileCollectionAssessmentOriginalIntent Original { get; init; }
    [JsonRequired] public required Guid SourceId { get; init; }
    [JsonRequired] public required long ExpectedSourceRevision { get; init; }
    [JsonRequired] public required long ExpectedSourceGeneration { get; init; }
    [JsonRequired] public required Guid AdoptionId { get; init; }
    [JsonRequired] public required long ExpectedAdoptionRevision { get; init; }
    [JsonRequired] public required long ExpectedAdoptionGeneration { get; init; }

    public ModerationProfileSubjectConsentAssessment ToAssessment() => new(
        Original.ToOriginal(), SourceId, ExpectedSourceRevision, ExpectedSourceGeneration,
        AdoptionId, ExpectedAdoptionRevision, ExpectedAdoptionGeneration);
}

/// <summary>Explicit self-consent nomination; the native owner authenticates the original subject and independently accepted assessment.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GrantModerationProfileSubjectConsentCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonRequired] public required ModerationProfileSubjectConsentAssessmentIntent Assessment { get; init; }
    [JsonRequired] public required Guid ConsentId { get; init; }
    [JsonRequired] public required long ExpectedGeneration { get; init; }
    [JsonRequired] public required DateTimeOffset ValidUntil { get; init; }

    public GrantModerationProfileSubjectConsent ToGrant() => new(
        Assessment.ToAssessment(), ConsentId, ExpectedGeneration, ValidUntil);
}

/// <summary>Terminal original-self correction; historical consent generation is distinct from current consent-slot CAS.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawModerationProfileSubjectConsentCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonRequired] public required Guid ConsentId { get; init; }
    [JsonRequired] public required long ExpectedConsentRevision { get; init; }
    [JsonRequired] public required long ExpectedConsentGeneration { get; init; }
    [JsonRequired] public required long ExpectedGeneration { get; init; }

    public WithdrawModerationProfileSubjectConsent ToWithdrawal() => new(
        ConsentId, ExpectedConsentRevision, ExpectedConsentGeneration, ExpectedGeneration);
}
