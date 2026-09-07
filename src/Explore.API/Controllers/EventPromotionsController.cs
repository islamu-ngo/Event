using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Promotions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.Promotions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}/promotions")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventPromotionsController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<PromotionManagementDto, PromotionManagementDto> assembler) : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string PromotionManagementNotFound = "promotion_management_not_found";

    private static readonly ApiValidationProblemDescriptor PromotionValidationProblem = new(
        "promotion",
        "Promotion request failed",
        "Promotion request failed.");

    private static readonly ApiNotFoundProblemDescriptor PromotionNotFoundProblem = new(
        "Promotion not found",
        "Promotion was not found.");

    private static readonly CommandFailurePolicy PromotionManagementFailures = CommandFailurePolicy
        .ValidatedBy(PromotionValidationProblem)
        .NotFound(PromotionNotFoundProblem, PromotionManagementNotFound);

    [HttpGet("", Name = RouteNames.GetEventPromotions)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<PromotionManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<PromotionManagementDto>>> List(
        Guid eventId,
        [FromQuery] Guid ticketCatalogVersionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PromotionManagementDto> promotions = await mediator.Send(
            new ListPromotionManagementQuery(eventId, ticketCatalogVersionId), cancellationToken);
        var resource = await assembler.ToCollectionResource(
            promotions,
            RouteNames.GetEventPromotions,
            new PromotionCollectionAuthorizationContext(eventId, ticketCatalogVersionId, tenantContext.TenantId),
            HttpContext);
        return Ok(resource);
    }

    [HttpGet("{promotionDefinitionId:guid}", Name = RouteNames.GetEventPromotion)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<PromotionManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<PromotionManagementDto>>> Get(
        Guid eventId,
        Guid promotionDefinitionId,
        CancellationToken cancellationToken = default)
    {
        PromotionManagementDto? promotion = await mediator.Send(
            new GetPromotionManagementQuery(eventId, promotionDefinitionId), cancellationToken);
        if (promotion is null)
        {
            return this.ToNotFoundProblem(PromotionNotFoundProblem);
        }

        var result = new ObjectResult(await assembler.ToResource(promotion, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    [HttpPost("", Name = RouteNames.CreateEventPromotionDraft)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionCodeIssuedCommandResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionCodeIssuedCommandResponseDto>> CreateDraft(
        Guid eventId,
        [FromBody] CreatePromotionDraftRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        PromotionCodeIssuedCommandResponseDto response = await mediator.Send(new CreatePromotionDraftCommand(
            eventId,
            request.TicketCatalogVersionId,
            request.DisplayLabel,
            request.Code,
            request.DiscountKind,
            request.FixedDiscountMinor,
            request.BasisPointDiscount,
            request.MaximumDiscountMinor,
            request.StartsAtUtc,
            request.EndsAtUtc,
            request.TotalRedemptionLimit,
            request.PerVerifiedPurchaserLimit,
            request.EligibleTicketTypeIds), cancellationToken);
        return response.IsSuccess
            ? CreatedAtRoute(RouteNames.GetEventPromotion, new { eventId, promotionDefinitionId = response.Id }, response)
            : PromotionManagementFailures.Map(this, response);
    }

    [HttpPut("{promotionDefinitionId:guid}", Name = RouteNames.ReviseEventPromotion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionManagementCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionManagementCommandResponseDto>> Revise(
        Guid eventId,
        Guid promotionDefinitionId,
        [FromBody] RevisePromotionRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapManagementSuccess(await mediator.Send(new RevisePromotionCommand(
        eventId,
        promotionDefinitionId,
        request.DisplayLabel,
        request.DiscountKind,
        request.FixedDiscountMinor,
        request.BasisPointDiscount,
        request.MaximumDiscountMinor,
        request.StartsAtUtc,
        request.EndsAtUtc,
        request.TotalRedemptionLimit,
        request.PerVerifiedPurchaserLimit,
        request.EligibleTicketTypeIds), cancellationToken));

    [HttpPost("{promotionDefinitionId:guid}/publish", Name = RouteNames.PublishEventPromotion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionManagementCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionManagementCommandResponseDto>> Publish(
        Guid eventId,
        Guid promotionDefinitionId,
        [FromBody] PromotionCodeRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapManagementSuccess(await mediator.Send(
        new PublishPromotionCommand(eventId, promotionDefinitionId, request.Code), cancellationToken));

    [HttpPost("{promotionDefinitionId:guid}/revoke", Name = RouteNames.RevokeEventPromotion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionManagementCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionManagementCommandResponseDto>> Revoke(
        Guid eventId,
        Guid promotionDefinitionId,
        [FromBody] RevokePromotionRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapManagementSuccess(await mediator.Send(
        new RevokePromotionCommand(eventId, promotionDefinitionId), cancellationToken));

    [HttpPost("{promotionDefinitionId:guid}/code:rotate", Name = RouteNames.RotateEventPromotionCode)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionCodeIssuedCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionCodeIssuedCommandResponseDto>> RotateCode(
        Guid eventId,
        Guid promotionDefinitionId,
        [FromBody] PromotionCodeRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapManagementSuccess(await mediator.Send(
        new RotatePromotionCodeCommand(eventId, promotionDefinitionId, request.Code), cancellationToken));

    private ActionResult<TResponse> MapManagementSuccess<TResponse>(TResponse response)
        where TResponse : BaseCommandResponse<Guid> =>
        response.IsSuccess ? Ok(response) : PromotionManagementFailures.Map(this, response);
}
