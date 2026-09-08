using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Explore.API.Extensions;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class StudioRegistrationOrderPaymentController(IMediator mediator) : RegistrationOrderPaymentControllerBase(mediator)
{
    private static readonly Explore.API.ExceptionHandling.ApiNotFoundProblemDescriptor PaymentNotFoundProblem = new(
        "Registration payment not found", "Registration payment was not found.");

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/payment/studio", Name = RouteNames.GetStudioRegistrationPayment)]
    [ProducesResponseType(typeof(HalResource<RegistrationPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationPaymentDto>>> GetStatus(
        Guid eventId, Guid orderId, CancellationToken cancellationToken = default)
    {
        RegistrationPaymentDto? payment = await Mediator.Send(new GetStudioRegistrationPaymentQuery(eventId, orderId), cancellationToken);
        if (payment is null)
        {
            return this.ToNotFoundProblem(PaymentNotFoundProblem);
        }

        var resource = new HalResource<RegistrationPaymentDto>(payment)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(RouteNames.GetStudioRegistrationPayment, new { eventId, orderId })!));
        if (payment.OrganizerRefundAvailable)
        {
            resource.WithLink(LinkRelations.CreateRefund, HalLink.CreateAction(
                Url.Link(RouteNames.CreateStudioRegistrationRefund, new { eventId, orderId })!, HttpMethods.Post));
        }
        RegistrationRefundDto? retryable = payment.Refunds.FirstOrDefault(refund => refund.ShouldAdvertiseSettlementRetry());
        if (retryable is not null)
        {
            resource.WithLink(LinkRelations.RetryRefund, HalLink.CreateAction(
                Url.Link(RouteNames.RetryStudioRegistrationRefund, new { eventId, orderId, refundAttemptId = retryable.Id })!,
                HttpMethods.Post));
        }
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment/studio/refunds", Name = RouteNames.CreateStudioRegistrationRefund)]
    [ProducesResponseType(typeof(HalResource<RegistrationRefundDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationRefundDto>>> CreateRefund(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        [FromBody] RegistrationRefundRequestDto request,
        CancellationToken cancellationToken = default) =>
        MapRefundResult(
            await Mediator.Send(new CreateStudioRegistrationRefundCommand(
                eventId, orderId, request, idempotencyKey), cancellationToken),
            eventId,
            orderId,
            RouteNames.GetStudioRegistrationPayment);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{orderId:guid}/payment/studio/refunds/{refundAttemptId:guid}/retry", Name = RouteNames.RetryStudioRegistrationRefund)]
    [ProducesResponseType(typeof(HalResource<RegistrationRefundDto>), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<HalResource<RegistrationRefundDto>>> RetryRefund(
        Guid eventId,
        Guid orderId,
        Guid refundAttemptId,
        [FromHeader(Name = IdempotencyKeyHeader)] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        return MapRefundResult(
            await Mediator.Send(
                new RetryStudioRegistrationRefundCommand(eventId, orderId, refundAttemptId), cancellationToken),
            eventId,
            orderId,
            RouteNames.GetStudioRegistrationPayment);
    }
}
