using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
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
public class EventSessionLanguageController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventSessionNotFoundProblem = new(
        "Event session not found",
        "Event session not found.");

    private static readonly ApiNotFoundProblemDescriptor EventSessionLanguageNotFoundProblem = new(
        "Event session language not found",
        "Event session language not found.");

    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session language creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "program",
        "Program validation failed",
        "Event session language update failed.");

    private static readonly ApiValidationProblemDescriptor IfMatchValidationProblem = new(
        "If-Match",
        "Program validation failed",
        "If-Match header is required and must contain the current event session language concurrency stamp.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventSessionLanguageDto, EventSessionLanguageListDto> _resourceAssembler;

    public EventSessionLanguageController(
        IMediator mediator,
        IResourceAssembler<EventSessionLanguageDto, EventSessionLanguageListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-session/{eventSessionId:guid}", Name = RouteNames.GetEventSessionLanguages)]
    [EndpointSummary("Get languages by event session")]
    [EndpointDescription("Get all language assignments for a specific event session.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionLanguageListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<EventSessionLanguageListDto>>> GetBySession(
        Guid eventSessionId,
        CancellationToken cancellationToken = default)
    {
        var languages = await _mediator.Send(new GetLanguagesBySessionRequest
        {
            EventSessionId = eventSessionId
        }, cancellationToken);

        var resource = await _resourceAssembler.ToCollectionResource(
            new PaginatedResult<EventSessionLanguageListDto>(languages, languages.Count, 1, Math.Max(languages.Count, 1)),
            RouteNames.GetEventSessionLanguages,
            new { eventSessionId },
            HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet(
        "management/by-event/{eventId:guid}/by-session/{eventSessionId:guid}",
        Name = RouteNames.GetManagedEventSessionLanguages)]
    [EndpointSummary("Get managed languages by event session")]
    [EndpointDescription("Get language assignments after view-management authorization on the parent event.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionLanguageListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventSessionLanguageListDto>>> GetManagedBySession(
        Guid eventId,
        Guid eventSessionId,
        CancellationToken cancellationToken = default)
    {
        var languages = await _mediator.Send(new GetManagedLanguagesBySessionRequest
        {
            EventId = eventId,
            EventSessionId = eventSessionId
        }, cancellationToken);

        var resource = await _resourceAssembler.ToCollectionResource(
            new PaginatedResult<EventSessionLanguageListDto>(languages, languages.Count, 1, Math.Max(languages.Count, 1)),
            RouteNames.GetManagedEventSessionLanguages,
            new { eventId, eventSessionId },
            HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSessionLanguage)]
    [EndpointSummary("Add language to event session")]
    [EndpointDescription("Assign a language to an event session.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<int>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<int>>> Create(
        [FromBody] CreateEventSessionLanguageDto language,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CreateEventSessionLanguageCommand
        {
            EventSessionLanguageDto = language
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSessionLanguages,
            new { eventSessionId = language.EventSessionId },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:int}", Name = RouteNames.UpdateEventSessionLanguage)]
    [EndpointSummary("Update event session language assignment")]
    [EndpointDescription("Update the session or language of an event session language assignment.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<int>>> Update(
        int id,
        [FromBody] UpdateEventSessionLanguageDto language,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                IfMatchValidationProblem,
                IfMatchValidationProblem.FallbackDetail);
        }

        var response = await _mediator.Send(new UpdateEventSessionLanguageCommand
        {
            EventSessionLanguageId = id,
            EventSessionLanguageDto = language,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventSessionLanguageNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:int}", Name = RouteNames.DeleteEventSessionLanguage)]
    [EndpointSummary("Remove language from event session")]
    [EndpointDescription("Remove a language assignment from an event session.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var language = await _mediator.Send(new GetEventSessionLanguageDetailsRequest { Id = id }, cancellationToken);
        if (language is null || language.Id == 0)
        {
            return this.ToNotFoundProblem(EventSessionLanguageNotFoundProblem);
        }

        var deleted = await _mediator.Send(new DeleteEventSessionLanguageCommand
        {
            Id = id,
            EventSessionId = language.EventSessionId
        }, cancellationToken);

        if (!deleted)
        {
            return this.ToNotFoundProblem(EventSessionLanguageNotFoundProblem);
        }

        return NoContent();
    }
}
