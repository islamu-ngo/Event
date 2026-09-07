namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class EventSessionSpeakerResourceAssembler
    : ResourceAssemblerBase<EventSessionSpeakerDto, EventSessionSpeakerListDto>
{
    private readonly IHateoasLinkGenerator _linkGenerator;

    public EventSessionSpeakerResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventSessionSpeakerDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventSessionSpeakerListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _linkGenerator = linkGenerator;
    }

    public override async Task<HalCollectionResource<EventSessionSpeakerListDto>> ToCollectionResource(
        IEnumerable<EventSessionSpeakerListDto> items,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var resource = await base.ToCollectionResource(items, routeName, additionalRouteValues, httpContext);

        if (additionalRouteValues is null || httpContext.User.Identity?.IsAuthenticated != true)
        {
            return resource;
        }

        var href = _linkGenerator.GeneratePath(RouteNames.CreateEventSessionSpeaker, additionalRouteValues, httpContext);
        if (href is not null)
        {
            resource.Links["create"] = new HalLink
            {
                Href = href,
                Method = "POST",
                Title = "Assign speaker"
            };
        }

        return resource;
    }
}
