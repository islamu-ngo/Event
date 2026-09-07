namespace Explore.API.Hateoas.Assemblers;

using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc.Controllers;

/// <summary>
/// Resource assembler for EventAgendaItem entities.
/// Converts EventAgendaItemDto and EventAgendaItemListDto to HAL resources.
/// </summary>
public sealed class EventAgendaItemResourceAssembler : ResourceAssemblerBase<EventAgendaItemDto, EventAgendaItemListDto>
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    public EventAgendaItemResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventAgendaItemDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventAgendaItemListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override Task<HalResource<EventAgendaItemDto>> ToResource(
        EventAgendaItemDto dto,
        HttpContext httpContext)
    {
        if (ResolveRouteName(httpContext) != RouteNames.GetManagedEventAgendaItemById)
        {
            Redact(dto);
        }

        return base.ToResource(dto, httpContext);
    }

    protected override ClaimsPrincipal? ResolveCapabilityPrincipal(HttpContext httpContext)
    {
        string? routeName = ResolveRouteName(httpContext);

        return routeName is RouteNames.GetManagedEventAgendaItemsByEvent
            or RouteNames.GetManagedEventAgendaItemById
            ? base.ResolveCapabilityPrincipal(httpContext)
            : AnonymousPrincipal;
    }

    private static string? ResolveRouteName(HttpContext httpContext) =>
        httpContext.GetEndpoint()?
            .Metadata.GetMetadata<ControllerActionDescriptor>()?
            .AttributeRouteInfo?.Name;

    private static void Redact(EventAgendaItemDto dto)
    {
        dto.LocationId = null;
        dto.RoomId = null;
    }

}
