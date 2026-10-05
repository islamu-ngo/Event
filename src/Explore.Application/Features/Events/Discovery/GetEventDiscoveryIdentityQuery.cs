using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Events.Discovery;

public sealed record GetEventDiscoveryIdentityQuery(Guid EventId, Guid? CandidateEventId = null)
    : IQuery<EventDiscoveryIdentityDto>;

/// <summary>Discovery guidance never substitutes the original record's participation identifiers.</summary>
public sealed record EventDiscoveryIdentityDto(
    Guid EventId,
    long? ExpectedRevision,
    Guid? PublicPrimaryEventId,
    Guid? ReviewTargetEventId,
    string? ReasonCode)
{
    [JsonIgnore] public bool IsManagementView { get; init; }
    [JsonIgnore] public bool CanViewCandidates { get; init; }
    [JsonIgnore] public bool CanReview { get; init; }
    [JsonIgnore] public bool CanReverse { get; init; }
}
