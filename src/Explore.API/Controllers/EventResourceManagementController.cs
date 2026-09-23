using System.Collections.Immutable;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api")]
[Tags("EventResourceManagement")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[RevalidateIdempotencyReplay]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceManagementController(
    ICommandHandler<CreateEventResourceCommand, BaseCommandResponse<Guid>> create,
    ICommandHandler<UpdateEventResourceCommand, BaseCommandResponse<Guid>> update,
    ICommandHandler<SetEventResourceDestinationCommand, BaseCommandResponse<Guid>> destination,
    ICommandHandler<PublishEventResourceCommand, BaseCommandResponse<Guid>> publish,
    ICommandHandler<UnpublishEventResourceCommand, BaseCommandResponse<Guid>> unpublish,
    ICommandHandler<ArchiveEventResourceCommand, BaseCommandResponse<Guid>> archive,
    ICommandHandler<DeleteEventResourceCommand, BaseCommandResponse<Guid>> delete,
    ICommandHandler<ModerateEventResourceCommand, BaseCommandResponse<Guid>> moderate,
    IQueryHandler<GetEventResourceManagementDetailQuery, EventResourceManagementReadResult<EventResourceManagementDto>> detail,
    IQueryHandler<ListEventResourceManagementQuery, EventResourceManagementReadResult<EventResourceManagementPageDto>> list,
    IQueryHandler<GetEventResourceAuditQuery, EventResourceManagementReadResult<EventResourceAuditPageDto>> audit,
    IQueryHandler<AuthorizeEventResourceManagementDisclosureQuery, EventResourceAuthorityOutcome> disclosure,
    IResourceAssembler<EventResourceManagementDto, EventResourceManagementDto> assembler,
    ITenantContext tenant) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor Missing = new(
        "Resource not found", "The requested resource was not found.");
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("eventResource", "Resource validation failed", "The resource request is invalid."))
        .NotFound(Missing, FailureCodes.NotFound)
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Resource authority required", "Current resource authority is required.", EventResourceManagementFailureCodes.Forbidden)
        .Conflict("Resource conflict", "Refresh the resource before retrying.",
            FailureCodes.ConcurrencyConflict, EventResourceManagementFailureCodes.CapacityExceeded,
            EventResourceManagementFailureCodes.PublicationUnavailable)
        .Unavailable("Resource authority unavailable", "Current resource authority could not be established.",
            EventResourceManagementFailureCodes.Unavailable);

    [HttpGet("eventresource/{id:guid}/management", Name = RouteNames.GetEventResourceManagementDetail)]
    [AllowAnonymous]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<EventResourceManagementDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<EventResourceManagementDto>>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await detail.QueryAsync(new(id), cancellationToken);
        if (result.Value is not { } value) return ReadFailure(result.Outcome);
        var representation = await assembler.ToResource(value, HttpContext);
        var outcome = await disclosure.QueryAsync(new(null, [value]), cancellationToken);
        return outcome == EventResourceAuthorityOutcome.Allowed ? Ok(representation) : ReadFailure(outcome);
    }

    [HttpGet("event/{eventId:guid}/resources/management", Name = RouteNames.ListEventResourceManagement)]
    [AllowAnonymous]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(EventResourceManagementCollectionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResourceManagementCollectionDto>> List(
        Guid eventId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await list.QueryAsync(new(eventId, page, pageSize), cancellationToken);
        if (result.Value is not { } value) return ReadFailure(result.Outcome);
        var representation = await assembler.ToCollectionResource(
            value.Items,
            RouteNames.ListEventResourceManagement,
            new EventMaterialCollectionAuthorizationContext(tenant.TenantId, eventId, value.Page, value.PageSize), HttpContext);
        var outcome = await disclosure.QueryAsync(new(eventId, value.Items), cancellationToken);
        return outcome == EventResourceAuthorityOutcome.Allowed
            ? Ok(new EventResourceManagementCollectionDto(value.Page, value.PageSize,
                representation.Links.ToImmutableDictionary(), representation.Embedded))
            : ReadFailure(outcome);
    }

    [HttpGet("eventresource/{id:guid}/audit", Name = RouteNames.GetEventResourceAudit)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventResourceAuditPageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResourceAuditPageDto>> Audit(
        Guid id, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var result = await audit.QueryAsync(new(id, limit), cancellationToken);
        return result.Value is { } value ? Ok(value) : ReadFailure(result.Outcome);
    }

    [HttpPost("event/{eventId:guid}/resources", Name = RouteNames.CreateEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        Guid eventId, [FromBody] CreateEventResourceRequestDto body, CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(new(eventId, body.ResourceId, body.Draft), cancellationToken);
        return Failures.Map(this, result, () => CreatedAtRoute(RouteNames.GetEventResourceManagementDetail,
            new { id = body.ResourceId }, result));
    }

    [HttpPut("eventresource/{id:guid}", Name = RouteNames.UpdateEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id, [FromBody] UpdateEventResourceRequestDto body, CancellationToken cancellationToken)
    {
        var result = await update.ExecuteAsync(new(id, body.ExpectedVersion, body.Draft), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpPut("eventresource/{id:guid}/destination", Name = RouteNames.SetEventResourceDestination)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetDestination(
        Guid id, [FromBody] EventResourceDestinationWriteDto input, CancellationToken cancellationToken)
    {
        var result = await destination.ExecuteAsync(
            new(id, input.ExpectedVersion, input.Destination), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpPost("eventresource/{id:guid}/publish", Name = RouteNames.PublishEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Publish(
        Guid id, [FromBody] EventResourceVersionRequestDto body, CancellationToken cancellationToken)
    {
        var result = await publish.ExecuteAsync(new(id, body.ExpectedVersion), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpPost("eventresource/{id:guid}/unpublish", Name = RouteNames.UnpublishEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Unpublish(
        Guid id, [FromBody] EventResourceVersionRequestDto body, CancellationToken cancellationToken)
    {
        var result = await unpublish.ExecuteAsync(new(id, body.ExpectedVersion), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpPost("eventresource/{id:guid}/archive", Name = RouteNames.ArchiveEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Archive(
        Guid id, [FromBody] EventResourceVersionRequestDto body, CancellationToken cancellationToken)
    {
        var result = await archive.ExecuteAsync(new(id, body.ExpectedVersion), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpDelete("eventresource/{id:guid}", Name = RouteNames.DeleteEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Delete(
        Guid id, [FromBody] EventResourceVersionRequestDto body, CancellationToken cancellationToken)
    {
        var result = await delete.ExecuteAsync(new(id, body.ExpectedVersion), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [HttpPost("eventresource/{id:guid}/moderate", Name = RouteNames.ModerateEventResource)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Moderate(
        Guid id, [FromBody] EventResourceVersionRequestDto body, CancellationToken cancellationToken)
    {
        var result = await moderate.ExecuteAsync(new(id, body.ExpectedVersion), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    private ActionResult ReadFailure(EventResourceAuthorityOutcome outcome) => outcome switch
    {
        EventResourceAuthorityOutcome.NotFound => this.ToNotFoundProblem(Missing),
        EventResourceAuthorityOutcome.AuthenticationRequired => this.ToAuthenticationRequiredProblem(),
        EventResourceAuthorityOutcome.Forbidden => this.ToForbiddenProblem(),
        _ => this.ToServiceUnavailableProblem("Resource authority unavailable",
            "Current resource authority could not be established.", EventResourceManagementFailureCodes.Unavailable)
    };
}
