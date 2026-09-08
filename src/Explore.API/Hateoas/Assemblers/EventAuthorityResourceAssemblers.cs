using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventOrganizerClaim;

namespace Explore.API.Hateoas.Assemblers;

public sealed class EventPublicActionResourceAssembler
    : ResourceAssemblerBase<EventPublicActionDto, EventPublicActionDto>
{
    public EventPublicActionResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventPublicActionDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventPublicActionDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}

public sealed class EventOrganizerClaimResourceAssembler
    : ResourceAssemblerBase<EventOrganizerClaimDto, EventOrganizerClaimDto>
{
    public EventOrganizerClaimResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventOrganizerClaimDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventOrganizerClaimDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}
