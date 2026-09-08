using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}/add-ons")]
public sealed class EventAddOnCatalogController(
    IMediator mediator,
    IResourceAssembler<EventAddOnCatalogDto, EventAddOnCatalogDto> assembler) :
    ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor Unavailable = new(
        "Add-on catalog unavailable",
        "The add-on catalog was not found or is unavailable.",
        "event_add_on_catalog_unavailable");

    [HttpGet("", Name = RouteNames.GetEventAddOnCatalog)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<EventAddOnCatalogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventAddOnCatalogDto>>> Get(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        EventAddOnCatalogDto? dto = await mediator.Send(
            new GetEventAddOnCatalogQuery(eventId, ManagementView: false),
            cancellationToken);
        return dto is null
            ? this.ToNotFoundProblem(Unavailable)
            : Ok(await assembler.ToResource(dto, HttpContext));
    }
}
