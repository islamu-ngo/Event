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
/// Administrative moderation endpoints for an event.
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
public class EventModerationController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor StatusValidationProblem = new(
        "event",
        "Event validation failed",
        "Event status update failed.");

    private static readonly ApiNotFoundProblemDescriptor EventNotFoundProblem = new(
        "Event not found",
        "Event not found.");

    private readonly IMediator _mediator;


    public EventModerationController(
        IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get authorized moderation audit history for an event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/moderation/history", Name = RouteNames.GetEventModerationHistory)]
    [EndpointSummary("Get Event Moderation History")]
    [EndpointDescription("Returns safe moderation audit metadata for authorized management views. Event text, slugs, URLs, image identifiers, and storage object paths are never included.")]
    [ProducesResponseType(typeof(IReadOnlyList<EventModerationHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EventModerationHistoryDto>>> GetModerationHistory(Guid id, CancellationToken cancellationToken = default)
    {
        var history = await _mediator.Send(new GetEventModerationHistoryRequest { Id = id }, cancellationToken);
        return history is null ? this.ToNotFoundProblem(EventNotFoundProblem) : Ok(history);
    }

    /// <summary>
    /// Hide an event after reversible administrative moderation.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/moderation/light", Name = RouteNames.ModerateEventLight)]
    [EndpointSummary("Light Moderate Event")]
    [EndpointDescription("Moves an event to the Moderated status using the reversible light-moderation authorization action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ModerateLight(
        Guid id,
        [FromBody] EventModerationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ModerateEventCommand
        {
            Id = id,
            ReasonCode = request.ReasonCode ?? string.Empty,
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, StatusValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Irreversibly redact unsafe event content after administrative moderation.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/moderation/heavy", Name = RouteNames.ModerateEventHeavy)]
    [EndpointSummary("Heavy Redact Event")]
    [EndpointDescription("Redacts event-owned text, detaches event images, queues provider-backed image deletion, and moves the event to the Moderated status using the heavy-moderation authorization action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ModerateHeavy(
        Guid id,
        [FromBody] EventModerationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new HeavyRedactEventCommand
        {
            Id = id,
            ReasonCode = request.ReasonCode ?? string.Empty,
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == HeavyRedactEventCommand.StorageDeletionPendingFailureCode
                ? this.ToServiceUnavailableProblem(
                    "Event heavy redaction image deletion pending",
                    response.Message ?? "Event heavy redaction completed, but image deletion is pending retry.",
                    response.FailureCode)
                : this.ToCommandValidationProblem(response, StatusValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Restore a reversibly moderated event to the published lifecycle state.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/moderation/unmoderate", Name = RouteNames.UnmoderateEvent)]
    [EndpointSummary("Unmoderate Event")]
    [EndpointDescription("Returns a reversibly light-moderated event to Published. Irreversible heavy redactions cannot be unmoderated.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Unmoderate(
        Guid id,
        [FromBody] EventModerationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UnmoderateEventCommand
        {
            Id = id,
            ReasonCode = request.ReasonCode ?? string.Empty,
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, StatusValidationProblem);
        }

        return Ok(response);
    }
}
