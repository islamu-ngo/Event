using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Agenda;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.Agenda.Requests.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Event Agenda Item management API endpoints.
/// Agenda items represent non-session schedule entries (breaks, meals, prayers, ceremonies).
/// Also provides a merged agenda projection combining sessions and agenda items.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventAgendaItemController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventAgendaItem",
        "Event agenda item validation failed",
        "Event agenda item creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventAgendaItem",
        "Event agenda item validation failed",
        "Event agenda item update failed.");

    private static readonly ApiNotFoundProblemDescriptor AgendaItemNotFoundProblem = new(
        "Event agenda item not found",
        "Event agenda item not found.");

    private static readonly ApiNotFoundProblemDescriptor AgendaProjectionNotFoundProblem = new(
        "Event agenda projection not found",
        "Event agenda projection not found.");

    private readonly IMediator _mediator;
    private readonly ILogger<EventAgendaItemController> _logger;
    private readonly IResourceAssembler<EventAgendaItemDto, EventAgendaItemListDto> _resourceAssembler;

    public EventAgendaItemController(
        IMediator mediator,
        ILogger<EventAgendaItemController> logger,
        IResourceAssembler<EventAgendaItemDto, EventAgendaItemListDto> resourceAssembler)
    {
        _mediator = mediator;
        _logger = logger;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get all agenda items for a specific event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-event/{eventId:guid}", Name = RouteNames.GetEventAgendaItemsByEvent)]
    [EndpointSummary("Get Agenda Items by Event")]
    [EndpointDescription("Get all agenda items for a specific event, ordered by sort order.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventAgendaItemListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<EventAgendaItemListDto>>> GetByEvent(Guid eventId, CancellationToken cancellationToken = default)
    {
        var items = await _mediator.Send(new GetEventAgendaItemsByEventRequest(eventId), cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            items,
            RouteNames.GetEventAgendaItemsByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event agenda item details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventAgendaItemById)]
    [EndpointSummary("Get Agenda Item Details")]
    [EndpointDescription("Get detailed information about a specific agenda item.")]
    [ProducesResponseType(typeof(HalResource<EventAgendaItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventAgendaItemDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _mediator.Send(new GetEventAgendaItemDetailRequest(id), cancellationToken);
        if (item == null)
            return this.ToNotFoundProblem(AgendaItemNotFoundProblem);

        var halResource = await _resourceAssembler.ToResource(item, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}", Name = RouteNames.GetManagedEventAgendaItemsByEvent)]
    [EndpointSummary("Get Managed Agenda Items by Event")]
    [EndpointDescription("Returns exact agenda items for an authorized event management surface.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventAgendaItemListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventAgendaItemListDto>>> GetManagedByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var items = await _mediator.Send(
            new GetManagedEventAgendaItemsByEventRequest { EventId = eventId },
            cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            items,
            RouteNames.GetManagedEventAgendaItemsByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}/{id:guid}", Name = RouteNames.GetManagedEventAgendaItemById)]
    [EndpointSummary("Get Managed Agenda Item Details")]
    [EndpointDescription("Returns exact agenda item details for an authorized event management surface.")]
    [ProducesResponseType(typeof(HalResource<EventAgendaItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventAgendaItemDto>>> GetManagedById(
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await _mediator.Send(new GetManagedEventAgendaItemDetailRequest
        {
            EventId = eventId,
            Id = id
        }, cancellationToken);
        if (item is null)
        {
            return this.ToNotFoundProblem(AgendaItemNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(item, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get the full agenda projection for an event, merging sessions and agenda items by day and room.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("agenda-projection/{eventId:guid}", Name = RouteNames.GetEventAgendaProjection)]
    [EndpointSummary("Get Event Agenda Projection")]
    [EndpointDescription("Get a merged view of all sessions and agenda items for an event, " +
        "grouped by local day and room with local time projections.")]
    [ProducesResponseType(typeof(EventAgendaProjectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventAgendaProjectionDto>> GetAgendaProjection(Guid eventId, CancellationToken cancellationToken = default)
    {
        var projection = await _mediator.Send(new GetEventAgendaProjectionRequest { EventId = eventId }, cancellationToken);
        if (projection == null)
            return this.ToNotFoundProblem(AgendaProjectionNotFoundProblem);

        return Ok(projection);
    }

    /// <summary>
    /// Create a new event agenda item.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventAgendaItem)]
    [EndpointSummary("Create Agenda Item")]
    [EndpointDescription("Create a new event agenda item. Must be associated with an existing event.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventAgendaItemDto agendaItem, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventAgendaItemCommand { EventAgendaItemDto = agendaItem };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventAgendaItemById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an existing event agenda item.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventAgendaItem)]
    [EndpointSummary("Update Agenda Item")]
    [EndpointDescription("Partially update an existing event agenda item. Route ID is authoritative and If-Match must contain the current agenda item concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventAgendaItemDto agendaItem,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event agenda item concurrency stamp.");
        }

        var command = new UpdateEventAgendaItemCommand
        {
            EventAgendaItemId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            EventAgendaItemDto = agendaItem
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(AgendaItemNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete an event agenda item.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventAgendaItem)]
    [EndpointSummary("Delete Agenda Item")]
    [EndpointDescription("Delete an event agenda item.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventAgendaItemCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
