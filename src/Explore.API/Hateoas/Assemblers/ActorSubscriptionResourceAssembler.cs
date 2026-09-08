namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ActorSubscription;

public sealed class ActorSubscriptionResourceAssembler : ResourceAssemblerBase<ActorSubscriptionDto, ActorSubscriptionListDto>
{
    public ActorSubscriptionResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<ActorSubscriptionDto> detailLinkPolicy,
        ICollectionLinkPolicy<ActorSubscriptionListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    protected override Dictionary<string, object>? GetEmbeddedResources(
        ActorSubscriptionDto dto,
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return null;
    }
}
