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
public sealed class AuthenticatedRegistrationOrderPaymentController(IMediator mediator) : RegistrationOrderPaymentControllerBase(mediator)
{
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment", Name = RouteNames.StartAuthenticatedRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> Start(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        [FromBody] PaidOrderAcceptanceAcknowledgementDto? acceptance,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapResult(await Mediator.Send(new StartAuthenticatedRegistrationPaymentCommand(eventId, orderId, acceptance), cancellationToken), eventId, orderId, false);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/payment/acceptance", Name = RouteNames.GetAuthenticatedPaidOrderAcceptance)]
    [ProducesResponseType(typeof(PaidOrderAcceptanceDisclosureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaidOrderAcceptanceDisclosureDto>> GetAcceptance(Guid eventId, Guid orderId, CancellationToken cancellationToken = default)
    {
        PaidOrderAcceptanceDisclosureDto? disclosure = await Mediator.Send(new GetAuthenticatedPaidOrderAcceptanceQuery(eventId, orderId), cancellationToken);
        return disclosure is null ? PaymentNotFoundResult() : Ok(disclosure);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/payment", Name = RouteNames.GetAuthenticatedRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> GetStatus(Guid eventId, Guid orderId, CancellationToken cancellationToken = default)
    {
        RegistrationPaymentDto? payment = await Mediator.Send(new GetAuthenticatedRegistrationPaymentQuery(eventId, orderId), cancellationToken);
        return payment is null ? PaymentNotFoundResult() : Ok(ToResource(payment, eventId, orderId, false));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment/retry", Name = RouteNames.RetryAuthenticatedRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> Retry(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapResult(await Mediator.Send(new RetryAuthenticatedRegistrationPaymentCommand(eventId, orderId), cancellationToken), eventId, orderId, false);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/payment/checkout-target", Name = RouteNames.GetAuthenticatedRegistrationPaymentCheckoutTarget)]
    [ProducesResponseType(typeof(RegistrationPaymentCheckoutTargetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationPaymentCheckoutTargetDto>> GetCheckoutTarget(Guid eventId, Guid orderId, CancellationToken cancellationToken = default) =>
        TargetOrNotFound(await Mediator.Send(new GetAuthenticatedRegistrationPaymentCheckoutTargetQuery(eventId, orderId), cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment/refunds", Name = RouteNames.RequestAuthenticatedRegistrationRefund)]
    [ProducesResponseType(typeof(HalResource<RegistrationRefundDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationRefundDto>>> RequestRefund(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        [FromBody] RegistrationRefundRequestDto request,
        CancellationToken cancellationToken = default) =>
        MapRefundResult(
            await Mediator.Send(new RequestAuthenticatedRegistrationRefundCommand(
                eventId, orderId, request, idempotencyKey), cancellationToken),
            eventId,
            orderId,
            RouteNames.GetAuthenticatedRegistrationPayment);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment/material-change-choice", Name = RouteNames.RespondAuthenticatedRegistrationMaterialChange)]
    [ProducesResponseType(typeof(HalResource<RegistrationMaterialChangeChoiceDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationMaterialChangeChoiceDto>>> RespondMaterialChange(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        [FromBody] RegistrationMaterialChangeChoiceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapMaterialChangeResult(
            await Mediator.Send(new RespondAuthenticatedRegistrationMaterialChangeCommand(
                eventId, orderId, request), cancellationToken),
            eventId,
            orderId);
    }
}
