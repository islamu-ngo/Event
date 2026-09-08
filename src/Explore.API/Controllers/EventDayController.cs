using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Event Day management API endpoints.
/// Event days represent individual days within a multi-day event.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventDayController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventDay",
        "Event day validation failed",
        "Event day creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventDay",
        "Event day validation failed",
        "Event day update failed.");

    private static readonly ApiNotFoundProblemDescriptor EventDayNotFoundProblem = new(
        "Event day not found",
        "Event day not found.");

    private readonly IMediator _mediator;
    private readonly ILogger<EventDayController> _logger;
    private readonly IResourceAssembler<EventDayDto, EventDayListDto> _resourceAssembler;

    public EventDayController(
        IMediator mediator,
        ILogger<EventDayController> logger,
        IResourceAssembler<EventDayDto, EventDayListDto> resourceAssembler)
    {
        _mediator = mediator;
        _logger = logger;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get all event days for a specific event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-event/{eventId:guid}", Name = RouteNames.GetEventDaysByEvent)]
    [EndpointSummary("Get Event Days by Event")]
    [EndpointDescription("Get all days for a specific event, ordered by sort order.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventDayListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<EventDayListDto>>> GetByEvent(Guid eventId, CancellationToken cancellationToken = default)
    {
        var days = await _mediator.Send(new GetEventDaysByEventRequest(eventId), cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            days,
            RouteNames.GetEventDaysByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get all event days for an event management view.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("management/by-event/{eventId:guid}", Name = RouteNames.GetManagedEventDaysByEvent)]
    [EndpointSummary("Get Managed Event Days by Event")]
    [EndpointDescription("Get all days for an event after view-management authorization.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventDayListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventDayListDto>>> GetManagedByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var days = await _mediator.Send(
            new GetManagedEventDaysByEventRequest { EventId = eventId },
            cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            days,
            RouteNames.GetManagedEventDaysByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event day details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventDayById)]
    [EndpointSummary("Get Event Day Details")]
    [EndpointDescription("Get detailed information about a specific event day.")]
    [ProducesResponseType(typeof(HalResource<EventDayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventDayDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var day = await _mediator.Send(new GetEventDayDetailRequest(id), cancellationToken);
        if (day == null)
            return this.ToNotFoundProblem(EventDayNotFoundProblem);

        var halResource = await _resourceAssembler.ToResource(day, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new event day.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventDay)]
    [EndpointSummary("Create Event Day")]
    [EndpointDescription("Create a new event day. Must be associated with an existing event.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventDayDto eventDay, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventDayCommand { EventDayDto = eventDay };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventDayById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an existing event day.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventDay)]
    [EndpointSummary("Update Event Day")]
    [EndpointDescription("Partially update an existing event day. Route ID is authoritative and If-Match must contain the current event day concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventDayDto eventDay,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event day concurrency stamp.");
        }

        var command = new UpdateEventDayCommand
        {
            EventDayId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            EventDayDto = eventDay
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventDayNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete an event day.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventDay)]
    [EndpointSummary("Delete Event Day")]
    [EndpointDescription("Delete an event day.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventDayCommand { Id = id };
        BaseCommandResponse<Guid> response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_day_ticket_entitlement_conflict"
                ? this.ToCommandConflictProblem(response, "Event day deletion conflict", "Event day deletion conflict.")
                : this.ToNotFoundProblem(EventDayNotFoundProblem);
        }

        return NoContent();
    }
}
