using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Event Session management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventSessionController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventSessionNotFoundProblem = new(
        "Event session not found",
        "Event session not found.");

    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session creation failed.");

    private static readonly ApiValidationProblemDescriptor CreateDraftValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session draft creation failed.");

    private static readonly ApiValidationProblemDescriptor ScheduleValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session schedule failed.");

    private static readonly ApiValidationProblemDescriptor PublishValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session publish failed.");

    private static readonly ApiValidationProblemDescriptor ArchiveValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session archive failed.");

    private static readonly ApiValidationProblemDescriptor CancelValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session cancel failed.");

    private static readonly ApiValidationProblemDescriptor CompleteValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session complete failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session update failed.");

    private readonly IMediator _mediator;
    private readonly ILogger<EventSessionController> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<EventSessionDto, EventSessionListDto> _resourceAssembler;

    public EventSessionController(
        IMediator mediator,
        ILogger<EventSessionController> logger,
        ITenantContext tenantContext,
        IResourceAssembler<EventSessionDto, EventSessionListDto> resourceAssembler)
    {
        _mediator = mediator;
        _logger = logger;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get all event sessions with pagination.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEventSessions_List)]
    [EndpointSummary("Get all Event Sessions")]
    [EndpointDescription("Get a paginated list of all event sessions. " +
        "Default page size is 20, max is 100. " +
        "Supports custom-property projection filters and text search gated behind the " +
        "tenant feature flag 'custom_properties.projection_discovery_enabled'. " +
        "Response includes HATEOAS navigation links. " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalCollectionResource<EventSessionListDto>>> GetAll(
        [FromQuery] EventSessionFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEventSessionListRequest
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            CustomPropertyFilters = filter.CustomPropertyFilters,
            CustomPropertySearchTerm = filter.CustomPropertySearchTerm
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEventSessions_List,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event session details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventSessionById)]
    [EndpointSummary("Get Event Session Details")]
    [EndpointDescription("Get detailed information about a specific event session. " +
        "Response includes links to related resources (event, speakers, agenda).")]
    [ProducesResponseType(typeof(HalResource<EventSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventSessionDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _mediator.Send(new GetEventSessionDetailsRequest { Id = id }, cancellationToken);
        if (session == null)
        {
            return this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(session, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}/{id:guid}", Name = RouteNames.GetManagedEventSessionById)]
    [EndpointSummary("Get Managed Event Session Details")]
    [EndpointDescription("Returns exact session details for an authorized event management surface.")]
    [ProducesResponseType(typeof(HalResource<EventSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventSessionDto>>> GetManagedById(
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var session = await _mediator.Send(new GetManagedEventSessionDetailsRequest
        {
            EventId = eventId,
            Id = id
        }, cancellationToken);
        if (session is null)
        {
            return this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(session, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get sessions for a specific event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-event/{eventId:guid}", Name = RouteNames.GetEventSessions)]
    [EndpointSummary("Get Sessions by Event")]
    [EndpointDescription("Get all sessions for a specific event.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<EventSessionListDto>>> GetByEvent(Guid eventId, CancellationToken cancellationToken = default)
    {
        var sessions = await _mediator.Send(new GetSessionsByEventRequest { EventId = eventId }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            sessions,
            RouteNames.GetEventSessions,
            HttpContext);

        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}", Name = RouteNames.GetManagedEventSessionsByEvent)]
    [EndpointSummary("Get Managed Sessions by Event")]
    [EndpointDescription("Returns all sessions for an event in management contexts, including draft/internal sessions hidden from public program routes.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventSessionListDto>>> GetManagedByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _mediator.Send(new GetManagedSessionsByEventRequest { EventId = eventId }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            sessions,
            RouteNames.GetManagedEventSessionsByEvent,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Create a new event session.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSession)]
    [EndpointSummary("Create Event Session")]
    [EndpointDescription("Create a new event session. Must be associated with an existing event.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventSessionDto session, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventSessionCommand
        {
            EventSessionDto = session,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSessionById,
            new { id = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("drafts", Name = RouteNames.CreateDraftEventSession)]
    [EndpointSummary("Create Draft Event Session")]
    [EndpointDescription("Creates an unscheduled draft session under an existing event.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateDraft(
        [FromBody] CreateDraftEventSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CreateDraftEventSessionCommand
        {
            TenantId = _tenantContext.TenantId,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateDraftValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetManagedEventSessionsByEvent,
            new { eventId = request.EventId },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/schedule", Name = RouteNames.ScheduleEventSession)]
    [EndpointSummary("Schedule Event Session")]
    [EndpointDescription("Schedules or reschedules an event session after readiness and concurrency validation.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Schedule(
        Guid id,
        [FromBody] ScheduleEventSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ScheduleEventSessionCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode is "event_session_schedule_concurrency_conflict" or "room_schedule_conflict"
                ? this.ToCommandConflictProblem(response, "Event session schedule conflict", "Event session schedule conflict.")
                : this.ToCommandValidationProblem(response, ScheduleValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/publish", Name = RouteNames.PublishEventSession)]
    [EndpointSummary("Publish Event Session")]
    [EndpointDescription("Publishes a scheduled event session after readiness and concurrency validation.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Publish(
        Guid id,
        [FromBody] PublishEventSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new PublishEventSessionCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_publish_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event session publish conflict", "Event session publish conflict.")
                : this.ToCommandValidationProblem(response, PublishValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/archive", Name = RouteNames.ArchiveEventSession)]
    [EndpointSummary("Archive Event Session")]
    [EndpointDescription("Archives an event session after concurrency and lifecycle validation.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Archive(
        Guid id,
        [FromBody] EventSessionLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ArchiveEventSessionCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_archive_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event session archive conflict", "Event session archive conflict.")
                : this.ToCommandValidationProblem(response, ArchiveValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/cancel", Name = RouteNames.CancelEventSession)]
    [EndpointSummary("Cancel Event Session")]
    [EndpointDescription("Cancels an event session after concurrency and lifecycle validation.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Cancel(
        Guid id,
        [FromBody] EventSessionLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CancelEventSessionCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_cancel_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event session cancel conflict", "Event session cancel conflict.")
                : this.ToCommandValidationProblem(response, CancelValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/complete", Name = RouteNames.CompleteEventSession)]
    [EndpointSummary("Complete Event Session")]
    [EndpointDescription("Completes a published event session after concurrency and lifecycle validation.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Complete(
        Guid id,
        [FromBody] EventSessionLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CompleteEventSessionCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_complete_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event session complete conflict", "Event session complete conflict.")
                : this.ToCommandValidationProblem(response, CompleteValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Partially update an existing event session.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventSession)]
    [EndpointSummary("Update Event Session")]
    [EndpointDescription("Partially update an existing event session. Route ID is authoritative and If-Match must contain the current event session concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSessionDto session,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event session concurrency stamp.");
        }

        var command = new UpdateEventSessionCommand
        {
            EventSessionId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            EventSessionDto = session
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventSessionNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete an event session.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventSession)]
    [EndpointSummary("Delete Event Session")]
    [EndpointDescription("Delete an event session.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventSessionCommand { Id = id };
        BaseCommandResponse<Guid> response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_ticket_entitlement_conflict"
                ? this.ToCommandConflictProblem(response, "Event session deletion conflict", "Event session deletion conflict.")
                : this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        return NoContent();
    }
}
