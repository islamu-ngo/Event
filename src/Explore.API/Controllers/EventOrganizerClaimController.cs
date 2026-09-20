using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/organizer-claims")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventOrganizerClaimController(
    IQueryHandler<GetEventOrganizerClaimsRequest, IReadOnlyList<EventOrganizerClaimDto>> getEventOrganizerClaimsHandler,
    IQueryHandler<GetEventOrganizerClaimRequest, EventOrganizerClaimDto?> getEventOrganizerClaimHandler,
    IQueryHandler<GetClaimantOrganizerClaimsRequest, IReadOnlyList<EventOrganizerClaimDto>> getClaimantOrganizerClaimsHandler,
    ICommandHandler<SubmitEventOrganizerClaimCommand, BaseCommandResponse<Guid>> submitHandler,
    ICommandHandler<WithdrawEventOrganizerClaimCommand, BaseCommandResponse<Guid>> withdrawHandler,
    ICommandHandler<ReviewEventOrganizerClaimCommand, BaseCommandResponse<Guid>> reviewHandler,
    IResourceAssembler<EventOrganizerClaimDto, EventOrganizerClaimDto> resourceAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor SubmitValidationProblem = new(
        "eventOrganizerClaim",
        "Event organizer claim validation failed",
        "Event organizer claim submission failed.");

    private static readonly ApiValidationProblemDescriptor WithdrawValidationProblem = new(
        "eventOrganizerClaim",
        "Event organizer claim validation failed",
        "Event organizer claim withdrawal failed.");

    private static readonly ApiValidationProblemDescriptor ReviewValidationProblem = new(
        "eventOrganizerClaim",
        "Event organizer claim validation failed",
        "Event organizer claim review failed.");

    private static readonly ApiNotFoundProblemDescriptor OrganizerClaimNotFoundProblem = new(
        "Event organizer claim not found",
        "The requested event organizer claim was not found.");

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("", Name = RouteNames.GetEventOrganizerClaims)]
    [EndpointSummary("Get event organizer claims")]
    [EndpointDescription("Returns organizer-claim evidence for an event after event-scoped authorization.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventOrganizerClaimDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<HalCollectionResource<EventOrganizerClaimDto>>> GetAll(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var claims = await getEventOrganizerClaimsHandler.QueryAsync(
            new GetEventOrganizerClaimsRequest(eventId),
            cancellationToken);

        var resource = await resourceAssembler.ToCollectionResource(
            claims,
            RouteNames.GetEventOrganizerClaims,
            new { eventId },
            HttpContext);

        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("{claimId:guid}", Name = RouteNames.GetEventOrganizerClaim)]
    [EndpointSummary("Get event organizer claim")]
    [EndpointDescription("Returns one organizer-claim evidence record for an event after event-scoped authorization.")]
    [ProducesResponseType(typeof(HalResource<EventOrganizerClaimDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<HalResource<EventOrganizerClaimDto>>> GetById(
        Guid eventId,
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        var claim = await getEventOrganizerClaimHandler.QueryAsync(
            new GetEventOrganizerClaimRequest(eventId, claimId),
            cancellationToken);
        if (claim is null)
        {
            return this.ToNotFoundProblem(OrganizerClaimNotFoundProblem);
        }

        return Ok(await resourceAssembler.ToResource(claim, HttpContext));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("~/api/actors/{claimantActorId:guid}/organizer-claims", Name = RouteNames.GetClaimantOrganizerClaims)]
    [EndpointSummary("Get claimant organizer claims")]
    [EndpointDescription("Returns organizer claims only when the authenticated user controls the claimant actor.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventOrganizerClaimDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<HalCollectionResource<EventOrganizerClaimDto>>> GetByClaimant(
        Guid claimantActorId,
        CancellationToken cancellationToken = default)
    {
        var claims = await getClaimantOrganizerClaimsHandler.QueryAsync(
            new GetClaimantOrganizerClaimsRequest(claimantActorId),
            cancellationToken);

        var resource = await resourceAssembler.ToCollectionResource(
            claims,
            RouteNames.GetClaimantOrganizerClaims,
            new { claimantActorId },
            HttpContext);

        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("", Name = RouteNames.SubmitEventOrganizerClaim)]
    [EndpointSummary("Submit event organizer claim")]
    [EndpointDescription("Submits bounded evidence requesting organizer authority over an event.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Submit(
        Guid eventId,
        [FromBody] SubmitEventOrganizerClaimDto claim,
        CancellationToken cancellationToken = default)
    {
        var response = await submitHandler.ExecuteAsync(
            new SubmitEventOrganizerClaimCommand
            {
                EventId = eventId,
                Claim = claim
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SubmitValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventOrganizerClaim,
            new { eventId, claimId = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("{claimId:guid}/withdraw", Name = RouteNames.WithdrawEventOrganizerClaim)]
    [EndpointSummary("Withdraw event organizer claim")]
    [EndpointDescription("Withdraws an organizer claim controlled by the authenticated claimant actor.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Withdraw(
        Guid eventId,
        Guid claimId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                WithdrawValidationProblem,
                "If-Match header is required and must contain the current event organizer claim concurrency stamp.");
        }

        var response = await withdrawHandler.ExecuteAsync(
            new WithdrawEventOrganizerClaimCommand
            {
                EventId = eventId,
                ClaimId = claimId,
                ExpectedConcurrencyStamp = expectedConcurrencyStamp
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, WithdrawValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("{claimId:guid}/review", Name = RouteNames.ReviewEventOrganizerClaim)]
    [EndpointSummary("Review event organizer claim")]
    [EndpointDescription("Applies an authenticated curator decision to an event organizer claim.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Review(
        Guid eventId,
        Guid claimId,
        [FromBody] ReviewEventOrganizerClaimDto review,
        CancellationToken cancellationToken = default)
    {
        var response = await reviewHandler.ExecuteAsync(
            new ReviewEventOrganizerClaimCommand
            {
                EventId = eventId,
                ClaimId = claimId,
                Review = review
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ReviewValidationProblem);
        }

        return Ok(response);
    }

}
