using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Features.ActorSubscriptions.Requests.Commands;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/actor-subscriptions")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ActorSubscriptionController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor SubscribeValidationProblem = new(
        "actorSubscription",
        "Actor subscription validation failed",
        "Actor subscription creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "actorSubscription",
        "Actor subscription validation failed",
        "Actor subscription update failed.");

    private static readonly ApiValidationProblemDescriptor UnsubscribeValidationProblem = new(
        "actorSubscription",
        "Actor subscription validation failed",
        "Actor subscription removal failed.");

    private static readonly ApiNotFoundProblemDescriptor ActorSubscriptionNotFoundProblem = new(
        "Actor subscription not found",
        "Actor subscription not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ActorSubscriptionDto, ActorSubscriptionListDto> _resourceAssembler;

    public ActorSubscriptionController(
        IMediator mediator,
        IResourceAssembler<ActorSubscriptionDto, ActorSubscriptionListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [HttpGet(Name = RouteNames.GetActorSubscriptions)]
    [EndpointSummary("Get current user's actor subscriptions")]
    [EndpointDescription("Retrieve the authenticated tenant user's actor subscriptions as HAL resources.")]
    [ProducesResponseType(typeof(HalCollectionResource<ActorSubscriptionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<ActorSubscriptionListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetActorSubscriptionsRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetActorSubscriptions,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    [HttpGet("actors/{targetActorId:guid}", Name = RouteNames.GetActorSubscriptionByActor)]
    [EndpointSummary("Get actor subscription state")]
    [EndpointDescription("Retrieve the authenticated tenant user's subscription state for one actor.")]
    [ProducesResponseType(typeof(HalResource<ActorSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<ActorSubscriptionDto>>> GetByActor(Guid targetActorId, CancellationToken cancellationToken = default)
    {
        var subscription = await _mediator.Send(new GetActorSubscriptionRequest
        {
            TargetActorId = targetActorId
        }, cancellationToken);

        if (subscription is null)
        {
            return this.ToNotFoundProblem(ActorSubscriptionNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(subscription, HttpContext);
        return Ok(halResource);
    }

    [HttpPost(Name = RouteNames.SubscribeToActor)]
    [EndpointSummary("Subscribe to actor")]
    [EndpointDescription("Subscribe the authenticated tenant user to an organization or group actor.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Subscribe([FromBody] SubscribeToActorDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new SubscribeToActorCommand { Subscription = dto }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SubscribeValidationProblem);
        }

        return Ok(response);
    }

    [HttpPatch("actors/{targetActorId:guid}/notification-level", Name = RouteNames.UpdateActorSubscriptionNotificationLevel)]
    [EndpointSummary("Update actor subscription notification level")]
    [EndpointDescription("Update notification delivery level for the authenticated tenant user's actor subscription.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateNotificationLevel(
        Guid targetActorId,
        [FromBody] UpdateActorSubscriptionNotificationLevelDto dto,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateActorSubscriptionNotificationLevelCommand
        {
            TargetActorId = targetActorId,
            Patch = dto
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "actor_subscription_not_found"
                ? this.ToNotFoundProblem(ActorSubscriptionNotFoundProblem, response.Message)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [HttpDelete("actors/{targetActorId:guid}", Name = RouteNames.UnsubscribeFromActor)]
    [EndpointSummary("Unsubscribe from actor")]
    [EndpointDescription("Unsubscribe the authenticated tenant user from an actor while preserving subscription history.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Unsubscribe(
        Guid targetActorId,
        [FromBody] UnsubscribeFromActorDto dto,
        CancellationToken cancellationToken = default)
    {
        dto = dto with { TargetActorId = targetActorId };

        var response = await _mediator.Send(new UnsubscribeFromActorCommand { Subscription = dto }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UnsubscribeValidationProblem);
        }

        return Ok(response);
    }
}
