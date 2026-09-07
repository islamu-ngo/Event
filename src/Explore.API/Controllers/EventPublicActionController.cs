using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventPublicAction;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/public-actions")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventPublicActionController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventPublicAction",
        "Event public action validation failed",
        "Event public action creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventPublicAction",
        "Event public action validation failed",
        "Event public action update failed.");

    private static readonly ApiValidationProblemDescriptor DeleteValidationProblem = new(
        "eventPublicAction",
        "Event public action validation failed",
        "Event public action deletion failed.");

    private static readonly ApiNotFoundProblemDescriptor PublicActionNotFoundProblem = new(
        "Event public action not found",
        "The requested event public action was not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventPublicActionDto, EventPublicActionDto> _resourceAssembler;

    public EventPublicActionController(
        IMediator mediator,
        IResourceAssembler<EventPublicActionDto, EventPublicActionDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("", Name = RouteNames.GetEventPublicActions)]
    [EndpointSummary("Get event public actions")]
    [EndpointDescription("Returns the active reviewed public actions for a published public event.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventPublicActionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalCollectionResource<EventPublicActionDto>>> GetAll(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var actions = await _mediator.Send(
            new GetEventPublicActionsRequest(eventId),
            cancellationToken);

        var resource = await _resourceAssembler.ToCollectionResource(
            actions,
            RouteNames.GetEventPublicActions,
            new { eventId },
            HttpContext);

        return Ok(resource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{actionId:guid}", Name = RouteNames.GetEventPublicAction)]
    [EndpointSummary("Get event public action")]
    [EndpointDescription("Returns one active reviewed public action for a published public event.")]
    [ProducesResponseType(typeof(HalResource<EventPublicActionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventPublicActionDto>>> GetById(
        Guid eventId,
        Guid actionId,
        CancellationToken cancellationToken = default)
    {
        var action = await _mediator.Send(
            new GetEventPublicActionRequest(eventId, actionId),
            cancellationToken);
        if (action is null)
        {
            return this.ToNotFoundProblem(PublicActionNotFoundProblem);
        }

        return Ok(await _resourceAssembler.ToResource(action, HttpContext));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{actionId:guid}/redirect", Name = RouteNames.RedirectEventPublicAction)]
    [EndpointSummary("Redirect to event public action")]
    [EndpointDescription("Redirects to the stored destination of an active reviewed public action.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult> RedirectToAction(
        Guid eventId,
        Guid actionId,
        [FromQuery] string? surface,
        CancellationToken cancellationToken = default)
    {
        var action = await _mediator.Send(
            new GetEventPublicActionRequest(eventId, actionId),
            cancellationToken);
        if (action is null)
        {
            return this.ToNotFoundProblem(PublicActionNotFoundProblem);
        }

        await _mediator.Send(
            new RecordEventPublicActionEngagementCommand((EventPublicActionKindEnum)action.KindId, surface),
            cancellationToken);

        return Redirect(action.Url);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("", Name = RouteNames.CreateEventPublicAction)]
    [EndpointSummary("Create event public action")]
    [EndpointDescription("Creates a reviewed event public action that starts in pending review.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        Guid eventId,
        [FromBody] ManageEventPublicActionDto action,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new CreateEventPublicActionCommand
            {
                EventId = eventId,
                Action = action
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventPublicAction,
            new { eventId, actionId = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPut("{actionId:guid}", Name = RouteNames.UpdateEventPublicAction)]
    [EndpointSummary("Update event public action")]
    [EndpointDescription("Replaces an event public action and returns it to pending review when its destination changes.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid eventId,
        Guid actionId,
        [FromBody] ManageEventPublicActionDto action,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new UpdateEventPublicActionCommand
            {
                EventId = eventId,
                ActionId = actionId,
                Action = action
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpDelete("{actionId:guid}", Name = RouteNames.DeleteEventPublicAction)]
    [EndpointSummary("Delete event public action")]
    [EndpointDescription("Soft-deletes an event public action using its current concurrency stamp.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Delete(
        Guid eventId,
        Guid actionId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                DeleteValidationProblem,
                "If-Match header is required and must contain the current event public action concurrency stamp.");
        }

        var response = await _mediator.Send(
            new DeleteEventPublicActionCommand
            {
                EventId = eventId,
                ActionId = actionId,
                ExpectedConcurrencyStamp = expectedConcurrencyStamp
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DeleteValidationProblem);
        }

        return Ok(response);
    }

}
