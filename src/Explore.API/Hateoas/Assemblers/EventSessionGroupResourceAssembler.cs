namespace Explore.API.Hateoas.Assemblers;

using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc.Controllers;

/// <summary>
/// Resource assembler for event session group entities.
/// </summary>
public sealed class EventSessionGroupResourceAssembler : ResourceAssemblerBase<EventSessionGroupDto, EventSessionGroupListDto>
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    public EventSessionGroupResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventSessionGroupDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventSessionGroupListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override Task<HalResource<EventSessionGroupDto>> ToResource(
        EventSessionGroupDto dto,
        HttpContext httpContext)
    {
        if (ResolveRouteName(httpContext) != RouteNames.GetManagedEventSessionGroupById)
        {
            Redact(dto);
        }

        return base.ToResource(dto, httpContext);
    }

    public override Task<HalCollectionResource<EventSessionGroupListDto>> ToCollectionResource(
        IEnumerable<EventSessionGroupListDto> items,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var itemList = items.ToList();
        if (routeName != RouteNames.GetManagedEventSessionGroupsByEvent)
        {
            foreach (var item in itemList)
            {
                Redact(item);
            }
        }

        return base.ToCollectionResource(itemList, routeName, additionalRouteValues, httpContext);
    }

    protected override ClaimsPrincipal? ResolveCapabilityPrincipal(HttpContext httpContext)
    {
        string? routeName = ResolveRouteName(httpContext);

        return routeName is RouteNames.GetManagedEventSessionGroupsByEvent
            or RouteNames.GetManagedEventSessionGroupById
            ? base.ResolveCapabilityPrincipal(httpContext)
            : AnonymousPrincipal;
    }

    private static string? ResolveRouteName(HttpContext httpContext) =>
        httpContext.GetEndpoint()?
            .Metadata.GetMetadata<ControllerActionDescriptor>()?
            .AttributeRouteInfo?.Name;

    private static void Redact(EventSessionGroupDto dto)
    {
        dto.LocationId = null;
        dto.LocationName = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }

    private static void Redact(EventSessionGroupListDto dto)
    {
        dto.LocationId = null;
        dto.LocationName = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }
}
