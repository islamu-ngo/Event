using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.Services.Calendar;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Controllers;

/// <summary>
/// Organizer-facing read contexts backing the event management surfaces.
/// </summary>
/// <remarks>
/// Split out of the original EventController by route capability. The route template is stated
/// explicitly rather than via the [controller] token so the public URLs are unchanged, and every action
/// keeps its original <c>Name = RouteNames.*</c>, which is what pins the generated operationId.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/Event")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventManagementReadController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventNotFoundProblem = new(
        "Event not found",
        "Event not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventDto, EventListDto> _resourceAssembler;


    public EventManagementReadController(
        IMediator mediator,
        IResourceAssembler<EventDto, EventListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get actor-owned events visible to the current principal for management/profile contexts.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("management/by-actor/{actorId:guid}", Name = RouteNames.GetManagedEventsByActor)]
    [EndpointSummary("Get Managed Events By Actor")]
    [EndpointDescription("Returns actor-owned events that the current principal is authorized to view in management contexts, including drafts and moderated events hidden from public discovery.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalCollectionResource<EventListDto>>> GetManagedByActor(
        Guid actorId,
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetManagedEventsByActorRequest
        {
            ActorId = actorId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetManagedEventsByActor,
            additionalRouteValues: new { actorId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get the current user's event creation context.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("creation-context", Name = RouteNames.GetEventCreationContext)]
    [EndpointSummary("Get Event Creation Context")]
    [EndpointDescription("Returns tenant event publishing policy and the personal, organization, and group publisher options available to the current user.")]
    [ProducesResponseType(typeof(EventCreationContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EventCreationContextDto>> GetCreationContext(CancellationToken cancellationToken = default)
    {
        var context = await _mediator.Send(new GetEventCreationContextRequest(), cancellationToken);
        return Ok(context);
    }

    /// <summary>
    /// Get server-owned defaults and selector options for adding a program item to an event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{id:guid}/session-create-context", Name = RouteNames.GetEventSessionCreateContext)]
    [EndpointSummary("Get Event Session Create Context")]
    [EndpointDescription("Returns inherited event defaults, location options, room options, and program sections for the dedicated program item composer.")]
    [ProducesResponseType(typeof(EventSessionCreateContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventSessionCreateContextDto>> GetSessionCreateContext(Guid id, CancellationToken cancellationToken = default)
    {
        var context = await _mediator.Send(new GetEventSessionCreateContextRequest { EventId = id }, cancellationToken);
        if (context is null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        return Ok(context);
    }

    /// <summary>
    /// Get the server-backed program summary for an event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}/program-summary", Name = RouteNames.GetEventProgramSummary)]
    [EndpointSummary("Get Event Program Summary")]
    [EndpointDescription("Returns program sections, local-day groupings, program items, and server-generated readiness warnings for the event program.")]
    [ProducesResponseType(typeof(EventProgramSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventProgramSummaryDto>> GetProgramSummary(Guid id, CancellationToken cancellationToken = default)
    {
        var summary = await _mediator.Send(new GetEventProgramSummaryRequest(id), cancellationToken);
        if (summary is null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        return Ok(summary);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{id:guid}/management-program-summary", Name = RouteNames.GetManagedEventProgramSummary)]
    [EndpointSummary("Get Managed Event Program Summary")]
    [EndpointDescription("Returns draft and published program sections, items, and readiness warnings for authorized event management.")]
    [ProducesResponseType(typeof(EventProgramSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventProgramSummaryDto>> GetManagedProgramSummary(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var summary = await _mediator.Send(new GetManagedEventProgramSummaryRequest { EventId = id }, cancellationToken);
        if (summary is null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        return Ok(summary);
    }

    /// <summary>
    /// Get authorized management event details by ID, including moderated events.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/management-detail", Name = RouteNames.GetEventManagementDetails)]
    [EndpointSummary("Get Event Management Details")]
    [EndpointDescription("Returns full event details for authorized management views, including moderated events that public detail routes intentionally hide.")]
    [ProducesResponseType(typeof(HalResource<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventDto>>> GetManagementDetails(Guid id, CancellationToken cancellationToken = default)
    {
        var @event = await _mediator.Send(new GetEventManagementDetailsRequest { Id = id }, cancellationToken);
        if (@event == null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        var halResource = await _resourceAssembler.ToResource(@event, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Review whether an event is ready to publish.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/publish-readiness", Name = RouteNames.GetEventPublishReadiness)]
    [EndpointSummary("Get Event Publish Readiness")]
    [EndpointDescription("Returns machine-readable readiness errors that block publishing an event.")]
    [ProducesResponseType(typeof(EventPublishReadinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventPublishReadinessDto>> GetPublishReadiness(Guid id, CancellationToken cancellationToken = default)
    {
        var readiness = await _mediator.Send(new GetEventPublishReadinessRequest { Id = id }, cancellationToken);
        return readiness is null ? this.ToNotFoundProblem(EventNotFoundProblem) : Ok(readiness);
    }
}
