using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Event session group API endpoints.
/// Event session groups represent tracks, devrooms, stages, and program sections within an event.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType, "application/problem+json")]
public class EventSessionGroupController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventSessionGroupNotFoundProblem = new(
        "Event session group not found",
        "Event session group not found.");

    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session group creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session group update failed.");

    private static readonly ApiValidationProblemDescriptor DeleteValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session group deletion failed.");

    private static readonly ApiValidationProblemDescriptor AssignmentValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session assignment failed.");

    private static readonly ApiValidationProblemDescriptor UnassignmentValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session unassignment failed.");

    private readonly IMediator _mediator;
    private readonly ILogger<EventSessionGroupController> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<EventSessionGroupDto, EventSessionGroupListDto> _resourceAssembler;
    private readonly IResourceAssembler<EventSessionDto, EventSessionListDto> _sessionResourceAssembler;

    public EventSessionGroupController(
        IMediator mediator,
        ILogger<EventSessionGroupController> logger,
        ITenantContext tenantContext,
        IResourceAssembler<EventSessionGroupDto, EventSessionGroupListDto> resourceAssembler,
        IResourceAssembler<EventSessionDto, EventSessionListDto> sessionResourceAssembler)
    {
        _mediator = mediator;
        _logger = logger;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
        _sessionResourceAssembler = sessionResourceAssembler;
    }

    /// <summary>
    /// Get all session groups for a specific event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-event/{eventId:guid}", Name = RouteNames.GetEventSessionGroupsByEvent)]
    [EndpointSummary("Get Event Session Groups by Event")]
    [EndpointDescription("Get tracks, devrooms, stages, or program sections for a specific event.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionGroupListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<EventSessionGroupListDto>>> GetByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var groups = await _mediator.Send(new GetEventSessionGroupsByEventRequest { EventId = eventId }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            groups,
            RouteNames.GetEventSessionGroupsByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event session group details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventSessionGroupById)]
    [EndpointSummary("Get Event Session Group Details")]
    [EndpointDescription("Get detailed information about a specific track, devroom, stage, or program section.")]
    [ProducesResponseType(typeof(HalResource<EventSessionGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventSessionGroupDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var group = await _mediator.Send(new GetEventSessionGroupDetailRequest { Id = id }, cancellationToken);
        if (group is null)
        {
            return this.ToNotFoundProblem(EventSessionGroupNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(group, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}", Name = RouteNames.GetManagedEventSessionGroupsByEvent)]
    [EndpointSummary("Get Managed Event Session Groups by Event")]
    [EndpointDescription("Returns exact program-section selectors for an authorized event management surface.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionGroupListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventSessionGroupListDto>>> GetManagedByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var groups = await _mediator.Send(
            new GetManagedEventSessionGroupsByEventRequest { EventId = eventId },
            cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            groups,
            RouteNames.GetManagedEventSessionGroupsByEvent,
            new { eventId },
            HttpContext);

        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management/by-event/{eventId:guid}/{id:guid}", Name = RouteNames.GetManagedEventSessionGroupById)]
    [EndpointSummary("Get Managed Event Session Group Details")]
    [EndpointDescription("Returns exact program-section details for an authorized event management surface.")]
    [ProducesResponseType(typeof(HalResource<EventSessionGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventSessionGroupDto>>> GetManagedById(
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var group = await _mediator.Send(new GetManagedEventSessionGroupDetailRequest
        {
            EventId = eventId,
            Id = id
        }, cancellationToken);
        if (group is null)
        {
            return this.ToNotFoundProblem(EventSessionGroupNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(group, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get sessions assigned to a specific event session group.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}/sessions", Name = RouteNames.GetEventSessionGroupSessions)]
    [EndpointSummary("Get Event Session Group Sessions")]
    [EndpointDescription("Get talks, workshops, panels, or activities assigned to a specific program section.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<EventSessionListDto>>> GetSessions(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _mediator.Send(
            new GetEventSessionGroupSessionsRequest { EventSessionGroupId = id },
            cancellationToken);

        var halResource = await _sessionResourceAssembler.ToCollectionResource(
            sessions,
            RouteNames.GetEventSessionGroupSessions,
            new { id },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Create a new event session group.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSessionGroup)]
    [EndpointSummary("Create Event Session Group")]
    [EndpointDescription("Create a track, devroom, stage, or program section for an event.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        [FromBody] CreateEventSessionGroupRequestDto group,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new CreateEventSessionGroupCommand
            {
                EventSessionGroup = group,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSessionGroupById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an event session group.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventSessionGroup)]
    [EndpointSummary("Update Event Session Group")]
    [EndpointDescription("Partially update a program section. Route identity is authoritative and If-Match must contain its current concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSessionGroupRequestDto group,
        [FromHeader(Name = "If-Match")] string ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event session group concurrency stamp.");
        }

        var response = await _mediator.Send(
            new UpdateEventSessionGroupCommand
            {
                EventSessionGroupId = id,
                ExpectedConcurrencyStamp = expectedConcurrencyStamp,
                EventSessionGroup = group,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete an event session group without deleting its sessions.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventSessionGroup)]
    [EndpointSummary("Delete Event Session Group")]
    [EndpointDescription("Delete a track, devroom, stage, or program section. Sessions remain intact.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        Guid id,
        [FromQuery] Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new DeleteEventSessionGroupCommand
            {
                Id = id,
                EventId = eventId,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DeleteValidationProblem);
        }

        return NoContent();
    }

    /// <summary>
    /// Assign an event session to a group.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/sessions", Name = RouteNames.AssignEventSessionToGroup)]
    [EndpointSummary("Assign Event Session to Group")]
    [EndpointDescription("Assign a talk, workshop, panel, or activity to a program section.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> AssignSession(
        Guid id,
        [FromBody] AssignSessionToGroupRequestDto assignment,
        CancellationToken cancellationToken = default)
    {
        if (id != assignment.EventSessionGroupId)
        {
            return this.ToValidationProblem(AssignmentValidationProblem, "Event session group ID mismatch.");
        }

        var response = await _mediator.Send(
            new AssignSessionToGroupCommand
            {
                Assignment = assignment,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, AssignmentValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Remove a session assignment from a group.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}/sessions/{sessionId:guid}", Name = RouteNames.UnassignEventSessionFromGroup)]
    [EndpointSummary("Unassign Event Session from Group")]
    [EndpointDescription("Remove a talk, workshop, panel, or activity from a program section without deleting the session.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnassignSession(
        Guid id,
        Guid sessionId,
        [FromQuery] Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new UnassignSessionFromGroupCommand
            {
                EventSessionGroupId = id,
                EventSessionId = sessionId,
                EventId = eventId,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UnassignmentValidationProblem);
        }

        return NoContent();
    }
}
