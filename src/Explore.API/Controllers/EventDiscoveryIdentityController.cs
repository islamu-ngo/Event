using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/event/{eventId:guid}/discovery-identity")]
[ApiController]
public sealed class EventDiscoveryIdentityController(
    IQueryHandler<GetEventDuplicateCandidatesQuery, EventDuplicateCandidatesDto> candidates,
    IQueryHandler<GetEventDiscoveryIdentityQuery, EventDiscoveryIdentityDto> identity,
    IResourceAssembler<EventDuplicateCandidatesDto, EventDuplicateCandidatesDto> candidateAssembler,
    IResourceAssembler<EventDiscoveryIdentityDto, EventDiscoveryIdentityDto> identityAssembler,
    ICommandHandler<ReviewEventDiscoveryAliasCommand, BaseCommandResponse<Guid>> review)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ReviewProblem = new(
        "eventDiscoveryIdentity", "Discovery identity review failed", "Discovery identity could not be corrected.");
    private static readonly CommandFailurePolicy ReviewFailure = CommandFailurePolicy.ValidatedBy(ReviewProblem)
        .Conflict("Discovery identity changed", "Reload the current identity before deciding.",
            FailureCodes.ConcurrencyConflict)
        .Unavailable("Identity review unavailable", "Try again after reloading the current identity.",
            "discovery_identity_unavailable")
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Identity review denied", "Current authority does not permit this decision.", FailureCodes.AdminRequired);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet(Name = RouteNames.GetEventDiscoveryIdentity)]
    [OutputCache(PolicyName = "PrivateNoStore")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HalResource<EventDiscoveryIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventDiscoveryIdentityDto>>> GetIdentity(
        Guid eventId, [FromQuery] Guid? candidateEventId, CancellationToken cancellationToken = default) =>
        Ok(await identityAssembler.ToResource(
            await identity.QueryAsync(new(eventId, candidateEventId), cancellationToken), HttpContext));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("candidates", Name = RouteNames.GetEventDuplicateCandidates)]
    [OutputCache(PolicyName = "PrivateNoStore")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HalResource<EventDuplicateCandidatesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventDuplicateCandidatesDto>>> GetCandidates(
        Guid eventId, CancellationToken cancellationToken = default) =>
        Ok(await candidateAssembler.ToResource(
            await candidates.QueryAsync(new GetEventDuplicateCandidatesQuery(eventId), cancellationToken), HttpContext));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("review", Name = RouteNames.ReviewEventDiscoveryAlias)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Review(
        Guid eventId, [FromBody] ReviewEventDiscoveryAliasDto decision,
        CancellationToken cancellationToken = default)
    {
        var response = await review.ExecuteAsync(
            new ReviewEventDiscoveryAliasCommand(eventId, decision), cancellationToken);
        return response.IsSuccess ? Ok(response) : ReviewFailure.Map(this, response);
    }
}
