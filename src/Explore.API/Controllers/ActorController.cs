using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Actor management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/actor")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[EndpointClassification(EndpointClass.Public)]
public class ActorController : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor ActorNotFoundProblem = new(
        "Actor not found",
        "Actor not found.");

    private static readonly ApiValidationProblemDescriptor ActorDidValidationProblem = new(
        "route",
        "Invalid route parameter",
        "The route parameter is invalid.");

    private static readonly ApiValidationProblemDescriptor GlobalModerationValidationProblem = new(
        "globalActorModeration",
        "Global actor moderation validation failed",
        "Global actor moderation failed.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ActorDto, ActorListDto> _resourceAssembler;

    public ActorController(
        IMediator mediator,
        IResourceAssembler<ActorDto, ActorListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get all actors with pagination.
    /// </summary>
    [HttpGet(Name = RouteNames.GetActors)]
    [EndpointSummary("Get all Actors")]
    [EndpointDescription("Retrieve a paginated list of all actors. " +
        "Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links. " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalCollectionResource<ActorListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<ActorListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetActorListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetActors,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get actor details by ID.
    /// </summary>
    [HttpGet("{id:guid}", Name = RouteNames.GetActorById)]
    [EndpointSummary("Get Actor Details")]
    [EndpointDescription("Get detailed information about a specific actor. " +
        "Response includes links to related resources (events, organization).")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalResource<ActorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<ActorDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var actor = await _mediator.Send(new GetActorDetailsRequest { Id = id }, cancellationToken);
        if (actor == null)
        {
            return this.ToNotFoundProblem(ActorNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(actor, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get actor by DID (Decentralized Identifier).
    /// </summary>
    [HttpGet("by-did/{did}", Name = RouteNames.GetActorByDid)]
    [EndpointSummary("Get Actor by DID")]
    [EndpointDescription("Get actor details using their decentralized identifier (DID).")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalResource<ActorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<ActorDto>>> GetByDid(string did, CancellationToken cancellationToken = default)
    {
        if (!AtprotoDid.TryParse(did, out var actorDid))
        {
            return this.ToIngressValidationProblem(ActorDidValidationProblem);
        }

        // GetActorByDidRequest remains scalar until its Application-owned Task 5.5 boundary migration.
        var actor = await _mediator.Send(new GetActorByDidRequest { Did = actorDid.Value }, cancellationToken);
        if (actor == null)
        {
            return this.ToNotFoundProblem(ActorNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(actor, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get actors by tenant.
    /// </summary>
    [HttpGet("by-tenant/{tenantId:guid}", Name = RouteNames.GetActorsByTenant)]
    [EndpointSummary("Get Actors by Tenant")]
    [EndpointDescription("Get all actors belonging to a specific tenant.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalCollectionResource<ActorListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<ActorListDto>>> GetByTenant(
        Guid tenantId,
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var actors = await _mediator.Send(new GetActorsByTenantRequest
        {
            TenantId = tenantId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            actors,
            RouteNames.GetActorsByTenant,
            HttpContext);

        return Ok(halResource);
    }

    [HttpGet("by-tenant/{tenantId:guid}/{id:guid}", Name = RouteNames.GetActorByTenant)]
    [EndpointSummary("Get Actor by Tenant")]
    [EndpointDescription("Get one locally discoverable actor with safe public participation overrides.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalResource<ActorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<ActorDto>>> GetContextual(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var actor = await _mediator.Send(new GetActorDetailsRequest
        {
            Id = id,
            TenantId = tenantId
        }, cancellationToken);
        if (actor is null)
        {
            return this.ToNotFoundProblem(ActorNotFoundProblem);
        }

        return Ok(await _resourceAssembler.ToResource(actor, HttpContext));
    }

    /// <summary>
    /// Suspend an actor globally.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{actorId:guid}/moderation/suspend", Name = RouteNames.SuspendActor)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Suspend Actor Globally")]
    [EndpointDescription("Suspends an actor globally using the server-selected moderation action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> SuspendActor(
        Guid actorId,
        [FromBody] GlobalModerationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ModerateActor(actorId, GlobalModerationAction.Suspend, request, cancellationToken);

    /// <summary>
    /// Reinstate an actor globally.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{actorId:guid}/moderation/reinstate", Name = RouteNames.ReinstateActor)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Reinstate Actor Globally")]
    [EndpointDescription("Reinstates an actor globally using the server-selected moderation action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> ReinstateActor(
        Guid actorId,
        [FromBody] GlobalModerationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ModerateActor(actorId, GlobalModerationAction.Reinstate, request, cancellationToken);

    /// <summary>
    /// Suspend an AT Protocol identity globally.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("atproto-identities/{identityId:guid}/moderation/suspend", Name = RouteNames.SuspendAtprotoIdentity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Suspend AT Protocol Identity Globally")]
    [EndpointDescription("Suspends an AT Protocol identity globally using the server-selected moderation action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> SuspendAtprotoIdentity(
        Guid identityId,
        [FromBody] GlobalModerationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ModerateAtprotoIdentity(identityId, GlobalModerationAction.Suspend, request, cancellationToken);

    /// <summary>
    /// Reinstate an AT Protocol identity globally.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("atproto-identities/{identityId:guid}/moderation/reinstate", Name = RouteNames.ReinstateAtprotoIdentity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Reinstate AT Protocol Identity Globally")]
    [EndpointDescription("Reinstates an AT Protocol identity globally using the server-selected moderation action.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> ReinstateAtprotoIdentity(
        Guid identityId,
        [FromBody] GlobalModerationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ModerateAtprotoIdentity(identityId, GlobalModerationAction.Reinstate, request, cancellationToken);

    private async Task<ActionResult<BaseCommandResponse<Guid>>> ModerateActor(
        Guid actorId,
        GlobalModerationAction action,
        GlobalModerationRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new ModerateActorCommand
        {
            ActorId = actorId,
            Moderation = new GlobalModerationRequest
            {
                Action = action,
                ReasonCode = request.ReasonCode
            }
        }, cancellationToken);

        return MapModerationResponse(response);
    }

    private async Task<ActionResult<BaseCommandResponse<Guid>>> ModerateAtprotoIdentity(
        Guid identityId,
        GlobalModerationAction action,
        GlobalModerationRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new ModerateAtprotoIdentityCommand
        {
            AtprotoIdentityId = identityId,
            Moderation = new GlobalModerationRequest
            {
                Action = action,
                ReasonCode = request.ReasonCode
            }
        }, cancellationToken);

        return MapModerationResponse(response);
    }

    private ActionResult<BaseCommandResponse<Guid>> MapModerationResponse(BaseCommandResponse<Guid> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        if (response.FailureCode == FailureCodes.AuthenticationRequired)
        {
            return this.ToAuthenticationRequiredProblem(detail: response.Message!);
        }

        return response.FailureCode == FailureCodes.AdminRequired
            ? this.ToForbiddenProblem(detail: response.Message)
            : this.ToCommandValidationProblem(response, GlobalModerationValidationProblem);
    }

}
