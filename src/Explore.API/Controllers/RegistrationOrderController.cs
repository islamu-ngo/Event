using Asp.Versioning;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class RegistrationOrderController(
    IMediator mediator,
    IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto> assembler)
    : RegistrationOrderControllerBase
{

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("checkout", Name = RouteNames.GetRegistrationCheckoutComposition)]
    [EndpointSummary("Get registration checkout composition")]
    [EndpointDescription("Returns the current published ticket choices for a publicly eligible platform-managed event.")]
    [ProducesResponseType(typeof(RegistrationCheckoutCompositionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationCheckoutCompositionDto>> GetCheckout(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new GetRegistrationCheckoutCompositionQuery(eventId), cancellationToken);
        return response is null ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem) : Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [PrivateNoStore]
    [HttpGet("", Name = RouteNames.GetEventRegistrationOrders)]
    [EndpointSummary("Get event registration orders")]
    [EndpointDescription("Returns registration orders for one event after event-scoped registration-management authorization.")]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationOrderDto>>> GetEventOrders(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrationOrderDto> orders = await mediator.Send(
            new GetEventRegistrationOrdersQuery(eventId),
            cancellationToken);
        HalCollectionResource<RegistrationOrderDto> resource = await assembler.ToCollectionResource(
            orders,
            RouteNames.GetEventRegistrationOrders,
            new { eventId },
            HttpContext);
        return Ok(resource);
    }

}
