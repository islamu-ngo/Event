namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Notification;

public sealed class WebPushSubscriptionResourceAssembler : ResourceAssemblerBase<WebPushSubscriptionDto>
{
    public WebPushSubscriptionResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<WebPushSubscriptionDto> detailLinkPolicy,
        ICollectionLinkPolicy<WebPushSubscriptionDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    protected override Dictionary<string, object>? GetEmbeddedResources(
        WebPushSubscriptionDto dto,
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return null;
    }
}
