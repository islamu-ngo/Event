using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
[Tags("GuestRegistrationOrder")]
public sealed class GuestRegistrationOrderPromotionsController(
    ICommandHandler<ApplyGuestPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto> applyGuestPromotionHandler,
    ICommandHandler<RemoveGuestPromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto> removeGuestPromotionHandler) : RegistrationOrderControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpPost("guest/{orderId:guid}/promotion", Name = RouteNames.ApplyGuestRegistrationOrderPromotion)]
    [EndpointSummary("Apply guest registration order promotion")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> ApplyGuestPromotion(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] PromotionCodeRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await applyGuestPromotionHandler.ExecuteAsync(
        new ApplyGuestPromotionCodeToRegistrationOrderCommand(eventId, orderId, capability, request.Code), cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpDelete("guest/{orderId:guid}/promotion", Name = RouteNames.RemoveGuestRegistrationOrderPromotion)]
    [EndpointSummary("Remove guest registration order promotion")]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> RemoveGuestPromotion(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await removeGuestPromotionHandler.ExecuteAsync(
        new RemoveGuestPromotionFromRegistrationOrderCommand(eventId, orderId, capability), cancellationToken));
}

