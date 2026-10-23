using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}/add-ons/management")]
public sealed class EventAddOnManagementController(
    IQueryHandler<GetEventAddOnCatalogQuery, EventAddOnCatalogDto?> getCatalogHandler,
    ICommandHandler<CreateEventAddOnCatalogDraftCommand, EventAddOnCatalogDto?> createDraftHandler,
    ICommandHandler<AddEventAddOnCatalogItemCommand, EventAddOnCatalogDto?> addItemHandler,
    ICommandHandler<PublishEventAddOnCatalogCommand, EventAddOnPublicationResponse> publishHandler,
    ICommandHandler<RetireEventAddOnCatalogCommand, EventAddOnCatalogDto?> retireHandler,
    IResourceAssembler<EventAddOnCatalogDto, EventAddOnCatalogDto> assembler,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor Unavailable = new(
        "Add-on management unavailable",
        "The add-on configuration was not found or is unavailable.",
        "event_add_on_management_unavailable");

    private static readonly CommandFailurePolicy PublicationFailures = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor("event_add_on", "Add-on publication failed", "Add-on publication failed."))
        .Forbidden("Add-on publication restricted", "Current publication restrictions prevent publishing this add-on catalog.",
            "event_add_on_publish_restricted")
        .Unavailable("Add-on publication authority unavailable", "Current publication authority could not be established.",
            "event_add_on_publish_authority_unavailable")
        .NotFound(Unavailable, "event_add_on_management_unavailable");

    [HttpGet("", Name = RouteNames.GetEventAddOnManagement)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<EventAddOnCatalogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<HalResource<EventAddOnCatalogDto>>> Get(
        Guid eventId,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            getCatalogHandler.QueryAsync(
                new GetEventAddOnCatalogQuery(eventId, ManagementView: true),
                cancellationToken));

    [HttpPost("draft", Name = RouteNames.CreateEventAddOnCatalogDraft)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType<HalResource<EventAddOnCatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<HalResource<EventAddOnCatalogDto>>> CreateDraft(
        Guid eventId,
        [FromBody] CreateEventAddOnCatalogDraftRequest request,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            createDraftHandler.ExecuteAsync(
                new CreateEventAddOnCatalogDraftCommand(eventId, request.CurrencyCode),
                cancellationToken));

    [HttpPost("items", Name = RouteNames.AddEventAddOnCatalogItem)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType<HalResource<EventAddOnCatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<HalResource<EventAddOnCatalogDto>>> AddItem(
        Guid eventId,
        [FromBody] ManageEventAddOnCatalogItemRequest request,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            addItemHandler.ExecuteAsync(
                new AddEventAddOnCatalogItemCommand(
                    eventId,
                    request.Name,
                    request.Description,
                    request.UnitPriceMinor,
                    request.InventoryCapacity,
                    request.FulfillmentDisclosure,
                    request.RefundDisclosure),
                cancellationToken));

    [HttpPost("publish", Name = RouteNames.PublishEventAddOnCatalog)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType<HalResource<EventAddOnCatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<EventAddOnCatalogDto>>> Publish(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        EventAddOnPublicationResponse response = await publishHandler.ExecuteAsync(
            new PublishEventAddOnCatalogCommand(eventId, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        if (!response.IsSuccess) return PublicationFailures.Map(this, response);
        return Ok(await assembler.ToResource(response.Catalog!, HttpContext));
    }

    [HttpPost("retire", Name = RouteNames.RetireEventAddOnCatalog)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType<HalResource<EventAddOnCatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<HalResource<EventAddOnCatalogDto>>> Retire(
        Guid eventId,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            retireHandler.ExecuteAsync(
                new RetireEventAddOnCatalogCommand(
                    eventId,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken));

    private async Task<ActionResult<HalResource<EventAddOnCatalogDto>>> ResourceAsync(
        Task<EventAddOnCatalogDto?> pending)
    {
        EventAddOnCatalogDto? dto = await pending;
        return dto is null
            ? this.ToNotFoundProblem(Unavailable)
            : Ok(await assembler.ToResource(dto, HttpContext));
    }
}
