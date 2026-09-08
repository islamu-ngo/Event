using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventDiscoveryLinkPolicy(ICollectionLinkPolicy<EventListDto> localPolicy)
    : ILinkPolicy<EventDiscoveryItemDto>, ICollectionLinkPolicy<EventDiscoveryItemDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventDiscoveryItemDto dto, ClaimsPrincipal? user) =>
        GetItemLinks(dto, user);

    public IEnumerable<LinkDefinition> GetItemLinks(EventDiscoveryItemDto dto, ClaimsPrincipal? user)
    {
        if (dto.Event is not null)
        {
            foreach (LinkDefinition link in localPolicy.GetItemLinks(dto.Event, user))
            {
                yield return link;
            }
        }

        if (dto.Federation is { HasSourceLink: true } federation)
        {
            yield return new LinkDefinition(
                "source",
                RouteNames.GetAtprotoEventSource,
                new { atprotoRecordId = federation.AtprotoRecordId },
                "GET",
                "Open original event source");
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        foreach (LinkDefinition link in localPolicy.GetCollectionLinks(user))
        {
            yield return link;
        }
    }
}
