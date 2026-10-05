using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventDiscoveryIdentityCollectionLinkPolicy : ICollectionLinkPolicy<EventDiscoveryIdentityDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventDiscoveryIdentityDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetEventDiscoveryIdentity, new { eventId = dto.EventId });
    }
}

public sealed class EventDuplicateCandidatesCollectionLinkPolicy : ICollectionLinkPolicy<EventDuplicateCandidatesDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventDuplicateCandidatesDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetEventDuplicateCandidates, new { eventId = dto.EventId });
    }
}
