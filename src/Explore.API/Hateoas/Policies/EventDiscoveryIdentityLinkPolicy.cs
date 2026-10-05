using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventDiscoveryIdentityLinkPolicy : ILinkPolicy<EventDiscoveryIdentityDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventDiscoveryIdentityDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetEventDiscoveryIdentity,
            new { eventId = dto.EventId, candidateEventId = dto.ReviewTargetEventId });
        yield return new LinkDefinition("original-event",
            dto.IsManagementView ? RouteNames.GetEventManagementDetails : RouteNames.GetEventById,
            new { id = dto.EventId }, "GET", RequiresAuth: dto.IsManagementView);
        if (dto.PublicPrimaryEventId is { } primaryId)
            yield return new LinkDefinition("canonical", RouteNames.GetEventById, new { id = primaryId }, "GET");
        if (dto.CanViewCandidates)
            yield return new LinkDefinition("candidates", RouteNames.GetEventDuplicateCandidates,
                new { eventId = dto.EventId }, "GET", RequiresAuth: true);
        if (dto.CanReview)
            yield return new LinkDefinition("review", RouteNames.ReviewEventDiscoveryAlias,
                new { eventId = dto.EventId }, "POST", RequiresAuth: true);
        if (dto.CanReverse)
            yield return new LinkDefinition("reverse", RouteNames.ReviewEventDiscoveryAlias,
                new { eventId = dto.EventId }, "POST", RequiresAuth: true);
    }
}

public sealed class EventDuplicateCandidatesLinkPolicy : ILinkPolicy<EventDuplicateCandidatesDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventDuplicateCandidatesDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetEventDuplicateCandidates, new { eventId = dto.EventId });
        yield return new LinkDefinition("discovery-identity", RouteNames.GetEventDiscoveryIdentity,
            new { eventId = dto.EventId }, "GET", RequiresAuth: true);
    }
}
