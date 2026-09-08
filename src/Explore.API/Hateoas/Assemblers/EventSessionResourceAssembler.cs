namespace Explore.API.Hateoas.Assemblers;

using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSession;
using Microsoft.AspNetCore.Mvc.Controllers;

/// <summary>
/// Resource assembler for EventSession entities.
/// Converts EventSessionDto and EventSessionListDto to HAL resources.
/// </summary>
public sealed class EventSessionResourceAssembler : ResourceAssemblerBase<EventSessionDto, EventSessionListDto>
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    public EventSessionResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventSessionDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventSessionListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    protected override ClaimsPrincipal? ResolveCapabilityPrincipal(HttpContext httpContext)
    {
        string? routeName = httpContext.GetEndpoint()?
            .Metadata.GetMetadata<ControllerActionDescriptor>()?
            .AttributeRouteInfo?.Name;

        return routeName is RouteNames.GetEventSessionById
            or RouteNames.GetEventSessions
            or RouteNames.GetEventSessions_List
            or RouteNames.GetEventSessionGroupSessions
            ? AnonymousPrincipal
            : base.ResolveCapabilityPrincipal(httpContext);
    }
}
