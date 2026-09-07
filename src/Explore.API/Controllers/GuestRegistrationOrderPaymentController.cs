using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class GuestRegistrationOrderPaymentController(IMediator mediator) : RegistrationOrderPaymentControllerBase(mediator)
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [RevalidateIdempotencyReplay]
    [PrivateNoStore]
    [HttpPost("guest/{orderId:guid}/payment", Name = RouteNames.StartGuestRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> Start(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        [FromBody] PaidOrderAcceptanceAcknowledgementDto? acceptance,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapResult(await Mediator.Send(new StartGuestRegistrationPaymentCommand(eventId, orderId, capability, acceptance), cancellationToken), eventId, orderId, true);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/payment/acceptance", Name = RouteNames.GetGuestPaidOrderAcceptance)]
    [ProducesResponseType(typeof(PaidOrderAcceptanceDisclosureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaidOrderAcceptanceDisclosureDto>> GetAcceptance(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability, CancellationToken cancellationToken = default)
    {
        PaidOrderAcceptanceDisclosureDto? disclosure = await Mediator.Send(
            new GetGuestPaidOrderAcceptanceQuery(eventId, orderId, capability), cancellationToken);
        return disclosure is null ? PaymentNotFoundResult() : Ok(disclosure);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/payment", Name = RouteNames.GetGuestRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> GetStatus(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability, CancellationToken cancellationToken = default)
    {
        RegistrationPaymentDto? payment = await Mediator.Send(new GetGuestRegistrationPaymentQuery(eventId, orderId, capability), cancellationToken);
        return payment is null ? PaymentNotFoundResult() : Ok(ToResource(payment, eventId, orderId, true));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [RevalidateIdempotencyReplay]
    [PrivateNoStore]
    [HttpPost("guest/{orderId:guid}/payment/retry", Name = RouteNames.RetryGuestRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> Retry(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapResult(await Mediator.Send(new RetryGuestRegistrationPaymentCommand(eventId, orderId, capability), cancellationToken), eventId, orderId, true);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/payment/checkout-target", Name = RouteNames.GetGuestRegistrationPaymentCheckoutTarget)]
    [ProducesResponseType(typeof(RegistrationPaymentCheckoutTargetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationPaymentCheckoutTargetDto>> GetCheckoutTarget(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability, CancellationToken cancellationToken = default) =>
        TargetOrNotFound(await Mediator.Send(new GetGuestRegistrationPaymentCheckoutTargetQuery(eventId, orderId, capability), cancellationToken));
}
