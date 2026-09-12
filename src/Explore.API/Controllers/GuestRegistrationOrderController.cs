using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Middleware;
using Explore.API.Models;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
[Tags("GuestRegistrationOrder")]
public sealed class GuestRegistrationOrderController(
    IMediator mediator,
    TimeProvider timeProvider) : RegistrationOrderControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.AnonymousRegistrationPolicy)]
    [RequireIdempotencyKey]
    [RequireAnonymousRegistrationChallenge]
    [PrivateNoStore]
    [ProtectIdempotencyReplay(CapabilityHeader, "Cache-Control", "Location")]
    [HttpPost("guest", Name = RouteNames.StartGuestRegistrationOrder)]
    [EndpointSummary("Start guest registration order")]
    [EndpointDescription("Creates an anonymous registration order and reveals its opaque recovery capability once in a response header.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(GuestRegistrationOrderStartDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GuestRegistrationOrderStartDto>> StartGuest(
        Guid eventId,
        [FromBody] StartRegistrationOrderRequest? request,
        CancellationToken cancellationToken = default,
        [FromHeader(Name = AnonymousRegistrationChallengeBoundary.ChallengeHeader)] string? challenge = null,
        [FromHeader(Name = AnonymousRegistrationChallengeBoundary.ProofHeader)] string? proof = null)
    {
        if (request is null)
        {
            return this.ToValidationProblem(RegistrationOrderValidationProblem, "A registration-order payload is required.");
        }

        GuestRegistrationOrderStartDto response = await mediator.Send(
            new StartGuestRegistrationOrderCommand(
                eventId,
                request.TicketCatalogVersionId,
                request.BookingPartyType,
                request.Lines,
                request.PlatformContributionBasisPoints)
            {
                ChallengeAuthority = AnonymousRegistrationChallengeBoundary.GetAuthority(HttpContext)
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return GuestStartFailures.Map(this, response);
        }

        Response.Headers[CapabilityHeader] = response.GuestCapabilityToken;
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtRoute(
            RouteNames.GetGuestRegistrationOrder,
            new { eventId, orderId = response.Id },
            response);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}", Name = RouteNames.GetGuestRegistrationOrder)]
    [EndpointSummary("Get guest registration order")]
    [EndpointDescription("Returns a registration order only when the route event, order, and opaque capability header match.")]
    [ProducesResponseType(typeof(HalResource<GuestRegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<GuestRegistrationOrderDto>>> GetGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        GuestRegistrationOrderDto? response = await mediator.Send(
            new GetGuestRegistrationOrderQuery(eventId, orderId, capability),
            cancellationToken);
        return response is null
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : Ok(GuestRegistrationOrderHalResourceFactory.Create(response, Url, timeProvider,
                await mediator.Send(new GetGuestRegistrationStatusQuery(eventId, orderId, capability), cancellationToken)));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [PrivateNoStore]
    [HttpPost("guest/{orderId:guid}/continue", Name = RouteNames.ContinueGuestRegistrationOrder)]
    [EndpointSummary("Continue guest registration order")]
    [EndpointDescription("Advances the guest order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(HalResource<GuestRegistrationOrderLifecycleResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<GuestRegistrationOrderLifecycleResponseDto>>> ContinueGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] ContinueRegistrationOrderRequest? request = null,
        CancellationToken cancellationToken = default) =>
        await MapGuestStatusLifecycle(await mediator.Send(
            new ContinueGuestRegistrationOrderCommand(
                eventId,
                orderId,
                capability,
                request?.PlatformContributionBasisPoints),
            cancellationToken), eventId, orderId, capability, cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [PrivateNoStore]
    [HttpPost("guest/{orderId:guid}/finalize", Name = RouteNames.FinalizeGuestRegistrationOrder)]
    [EndpointSummary("Finalize guest registration order")]
    [EndpointDescription("Finalizes a free guest registration order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(HalResource<GuestRegistrationOrderLifecycleResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<GuestRegistrationOrderLifecycleResponseDto>>> FinalizeGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default) =>
        await MapGuestStatusLifecycle(await mediator.Send(
            new FinalizeGuestRegistrationOrderCommand(eventId, orderId, capability),
            cancellationToken), eventId, orderId, capability, cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpDelete("guest/{orderId:guid}", Name = RouteNames.CancelGuestRegistrationOrder)]
    [EndpointSummary("Cancel guest registration order")]
    [EndpointDescription("Cancels a guest registration order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(GuestRegistrationOrderLifecycleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GuestRegistrationOrderLifecycleResponseDto>> CancelGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default) =>
        MapGuestLifecycle(await mediator.Send(
            new CancelGuestRegistrationOrderCommand(eventId, orderId, capability),
            cancellationToken));

    private async Task<ActionResult<HalResource<GuestRegistrationOrderLifecycleResponseDto>>> MapGuestStatusLifecycle(
        GuestRegistrationOrderLifecycleResponseDto response, Guid eventId, Guid orderId, string? capability,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccess)
        {
            return MapGuestLifecycle(response).Result!;
        }

        var resource = new HalResource<GuestRegistrationOrderLifecycleResponseDto>(response);
        GuestRegistrationStatusDto? status = await mediator.Send(
            new GetGuestRegistrationStatusQuery(eventId, orderId, capability), cancellationToken);
        if (status is not null)
        {
            resource.WithLink(LinkRelations.GuestStatus, HalLink.Create(Url.Link(
                RouteNames.GetGuestRegistrationStatus, new { eventId = status.EventId, orderId = status.OrderId })!));
        }
        return Ok(resource);
    }

    /// <summary>A missing order graph is not-found even without the code: the response cannot describe the resource.</summary>
    private ActionResult<GuestRegistrationOrderLifecycleResponseDto> MapGuestLifecycle(
        GuestRegistrationOrderLifecycleResponseDto response) =>
        response.IsSuccess
            ? Ok(response)
            : response.Order is null
                ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
                : OrderLifecycleFailures.Map(this, response);
}
