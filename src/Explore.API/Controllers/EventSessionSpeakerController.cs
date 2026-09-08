using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventSessionSpeakerController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventSessionNotFoundProblem = new(
        "Event session not found",
        "Event session not found.");

    private static readonly ApiNotFoundProblemDescriptor EventSessionSpeakerNotFoundProblem = new(
        "Event session speaker assignment not found",
        "Event session speaker assignment not found.");

    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventSessionSpeaker",
        "Event session speaker validation failed",
        "Event session speaker creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventSessionSpeaker",
        "Event session speaker validation failed",
        "Event session speaker update failed.");

    private static readonly ApiValidationProblemDescriptor IfMatchValidationProblem = new(
        "If-Match",
        "Event session speaker validation failed",
        "If-Match header is required and must contain the current event session speaker concurrency stamp.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventSessionSpeakerDto, EventSessionSpeakerListDto> _assembler;

    public EventSessionSpeakerController(
        IMediator mediator,
        IResourceAssembler<EventSessionSpeakerDto, EventSessionSpeakerListDto> assembler)
    {
        _mediator = mediator;
        _assembler = assembler;
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("management/by-session/{eventSessionId:guid}", Name = RouteNames.GetEventSessionSpeakersBySession)]
    [EndpointSummary("Get speaker assignments by event session")]
    [EndpointDescription("Get management speaker assignment rows for a specific event session.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionSpeakerListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<EventSessionSpeakerListDto>>> GetBySession(
        Guid eventSessionId,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextOrNullAsync(eventSessionId, cancellationToken);
        if (context is null)
        {
            return this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        var speakers = await _mediator.Send(new GetSpeakersBySessionRequest
        {
            EventSessionId = eventSessionId
        }, cancellationToken);

        var resource = await _assembler.ToCollectionResource(
            speakers,
            RouteNames.GetEventSessionSpeakersBySession,
            new { eventSessionId },
            HttpContext);

        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("management/by-session/{eventSessionId:guid}", Name = RouteNames.CreateEventSessionSpeaker)]
    [EndpointSummary("Assign speaker to event session")]
    [EndpointDescription("Assign an actor as a speaker for an event session.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        Guid eventSessionId,
        [FromBody] CreateEventSessionSpeakerDto speaker,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextOrNullAsync(eventSessionId, cancellationToken);
        if (context is null)
        {
            return this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        speaker = speaker with { EventSessionId = eventSessionId };

        var response = await _mediator.Send(new CreateEventSessionSpeakerCommand
        {
            SpeakerDto = speaker,
            EventId = context.EventId,
            TenantId = context.TenantId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSessionSpeakersBySession,
            new { eventSessionId },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("management/{id:guid}", Name = RouteNames.UpdateEventSessionSpeaker)]
    [EndpointSummary("Update event session speaker assignment")]
    [EndpointDescription("Update the actor or target session for an event session speaker assignment.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSessionSpeakerDto speaker,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                IfMatchValidationProblem,
                IfMatchValidationProblem.FallbackDetail);
        }

        var response = await _mediator.Send(new UpdateEventSessionSpeakerCommand
        {
            EventSessionSpeakerId = id,
            SpeakerDto = speaker,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_session_speaker_not_found"
                ? this.ToNotFoundProblem(EventSessionSpeakerNotFoundProblem, response.Message)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("management/by-session/{eventSessionId:guid}/{id:guid}", Name = RouteNames.DeleteEventSessionSpeaker)]
    [EndpointSummary("Remove speaker from event session")]
    [EndpointDescription("Remove a speaker assignment from an event session.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        Guid eventSessionId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextOrNullAsync(eventSessionId, cancellationToken);
        if (context is null)
        {
            return this.ToNotFoundProblem(EventSessionNotFoundProblem);
        }

        var deleted = await _mediator.Send(new DeleteEventSessionSpeakerCommand
        {
            Id = id,
            EventSessionId = eventSessionId,
            TenantId = context.TenantId,
            EventId = context.EventId
        }, cancellationToken);

        if (!deleted)
        {
            return this.ToNotFoundProblem(EventSessionSpeakerNotFoundProblem);
        }

        return NoContent();
    }

    private async Task<EventSessionAuthorizationContextDto?> GetSessionContextOrNullAsync(
        Guid eventSessionId,
        CancellationToken cancellationToken)
    {
        if (eventSessionId == Guid.Empty)
        {
            return null;
        }

        return await _mediator.Send(new GetEventSessionAuthorizationContextRequest
        {
            EventSessionId = eventSessionId
        }, cancellationToken);
    }
}
