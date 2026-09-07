using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/custom-property-projections")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class CustomPropertyProjectionAdminController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor RebuildSingleEventValidationProblem = new(
        "customPropertyProjection",
        "Custom-property projection validation failed",
        "Single event custom-property projection rebuild failed.");

    private static readonly ApiValidationProblemDescriptor DrainDirtyScopesValidationProblem = new(
        "customPropertyProjection",
        "Custom-property projection validation failed",
        "Custom-property projection dirty-scope drain failed.");

    private static readonly ApiValidationProblemDescriptor RebuildSingleSessionValidationProblem = new(
        "eventSessionCustomPropertyProjection",
        "Event session custom-property projection validation failed",
        "Single event session custom-property projection rebuild failed.");

    private static readonly ApiValidationProblemDescriptor EventProjectionLookupValidationProblem = new(
        "customPropertyProjection",
        "Custom-property projection validation failed",
        "Event custom-property projection lookup failed.");

    private static readonly ApiValidationProblemDescriptor EventProjectionStatusValidationProblem = new(
        "customPropertyProjection",
        "Custom-property projection validation failed",
        "Projection status lookup failed.");

    private static readonly ApiValidationProblemDescriptor SessionProjectionLookupValidationProblem = new(
        "eventSessionCustomPropertyProjection",
        "Event session custom-property projection validation failed",
        "Event session custom-property projection lookup failed.");

    private static readonly ApiValidationProblemDescriptor SessionProjectionStatusValidationProblem = new(
        "eventSessionCustomPropertyProjection",
        "Event session custom-property projection validation failed",
        "Session projection status lookup failed.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ProjectionStatusDto, ProjectionStatusDto> _statusAssembler;
    private readonly IResourceAssembler<ProjectionDirtyScopeDto, ProjectionDirtyScopeDto> _dirtyScopeAssembler;

    public CustomPropertyProjectionAdminController(
        IMediator mediator,
        IResourceAssembler<ProjectionStatusDto, ProjectionStatusDto> statusAssembler,
        IResourceAssembler<ProjectionDirtyScopeDto, ProjectionDirtyScopeDto> dirtyScopeAssembler)
    {
        _mediator = mediator;
        _statusAssembler = statusAssembler;
        _dirtyScopeAssembler = dirtyScopeAssembler;
    }

    /// <summary>
    /// Get projection status for a tenant's event custom-property projections.
    /// </summary>
    [HttpGet("status", Name = RouteNames.GetCustomPropertyProjectionStatus)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<ProjectionStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalCollectionResource<ProjectionStatusDto>>> GetProjectionStatus(
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetEventCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToCommandValidationProblem(result, EventProjectionStatusValidationProblem);
        }

        var halResource = await _statusAssembler.ToCollectionResource(
            result.Id ?? [],
            RouteNames.GetCustomPropertyProjectionStatus,
            new { tenantId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Rebuild event custom-property projections for a tenant.
    /// </summary>
    [HttpPost("rebuild", Name = RouteNames.RebuildCustomPropertyProjection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<RebuildProjectionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BaseCommandResponse<RebuildProjectionResponseDto>>> RebuildProjection(
        [FromBody] RebuildProjectionRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RebuildEventCustomPropertyProjectionCommand { RequestDto = requestDto },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToQuotaProblemOrBadRequest(result);
    }

    /// <summary>
    /// Rebuild projection rows for a single event.
    /// </summary>
    [HttpPost("rebuild-single-event", Name = RouteNames.RebuildSingleEventCustomPropertyProjection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RebuildSingleEventProjection(
        [FromBody] RebuildSingleEventProjectionRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RebuildSingleEventCustomPropertyProjectionCommand { EventId = requestDto.EventId },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, RebuildSingleEventValidationProblem);
    }

    /// <summary>
    /// Drain pending dirty scopes for a projection without triggering a full rebuild.
    /// </summary>
    [HttpPost("drain-dirty-scopes", Name = RouteNames.DrainCustomPropertyProjectionDirtyScopes)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<DrainDirtyScopesResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<DrainDirtyScopesResponseDto>>> DrainDirtyScopes(
        [FromBody] DrainDirtyScopesRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new DrainCustomPropertyProjectionDirtyScopesCommand { RequestDto = requestDto },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, DrainDirtyScopesValidationProblem);
    }

    /// <summary>
    /// Get pending dirty-scope backlog rows for operator inspection.
    /// </summary>
    [HttpGet("dirty-scopes", Name = RouteNames.GetCustomPropertyProjectionDirtyScopes)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<ProjectionDirtyScopeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalCollectionResource<ProjectionDirtyScopeDto>>> GetDirtyScopes(
        [FromQuery] CustomPropertyProjectionDirtyScopesQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetCustomPropertyProjectionDirtyScopesQuery
            {
                TenantId = query.TenantId,
                ProjectionName = query.ProjectionName!,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            },
            cancellationToken);

        var halResource = await _dirtyScopeAssembler.ToCollectionResource(
            result,
            RouteNames.GetCustomPropertyProjectionDirtyScopes,
            new { query.TenantId, query.ProjectionName },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get projection rows for a specific event.
    /// </summary>
    [HttpGet("events/{eventId:guid}", Name = RouteNames.GetCustomPropertyProjectionsForEvent)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>> GetProjectionsForEvent(
        Guid eventId,
        [FromQuery] ExposureLevel? exposureCeiling = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetEventCustomPropertyProjectionsForEventQuery
            {
                EventId = eventId,
                ExposureCeiling = exposureCeiling
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, EventProjectionLookupValidationProblem);
    }

    // ── Session projection endpoints ───────────────────────────────────────

    /// <summary>
    /// Get session projection status for a tenant.
    /// </summary>
    [HttpGet("sessions/status", Name = RouteNames.GetSessionCustomPropertyProjectionStatus)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<ProjectionStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalCollectionResource<ProjectionStatusDto>>> GetSessionProjectionStatus(
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetEventSessionCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToCommandValidationProblem(result, SessionProjectionStatusValidationProblem);
        }

        var halResource = await _statusAssembler.ToCollectionResource(
            result.Id ?? [],
            RouteNames.GetSessionCustomPropertyProjectionStatus,
            new { tenantId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Rebuild event session custom-property projections for a tenant.
    /// </summary>
    [HttpPost("sessions/rebuild", Name = RouteNames.RebuildSessionCustomPropertyProjection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<RebuildProjectionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BaseCommandResponse<RebuildProjectionResponseDto>>> RebuildSessionProjection(
        [FromBody] RebuildProjectionRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RebuildEventSessionCustomPropertyProjectionCommand { RequestDto = requestDto },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToQuotaProblemOrBadRequest(result);
    }

    /// <summary>
    /// Rebuild projection rows for a single event session.
    /// </summary>
    [HttpPost("sessions/rebuild-single", Name = RouteNames.RebuildSingleSessionCustomPropertyProjection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RebuildSingleSessionProjection(
        [FromBody] RebuildSingleEventSessionProjectionRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RebuildSingleEventSessionCustomPropertyProjectionCommand { EventSessionId = requestDto.EventSessionId },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, RebuildSingleSessionValidationProblem);
    }

    /// <summary>
    /// Get projection rows for a specific event session.
    /// </summary>
    [HttpGet("sessions/{eventSessionId:guid}", Name = RouteNames.GetCustomPropertyProjectionsForSession)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>> GetProjectionsForSession(
        Guid eventSessionId,
        [FromQuery] ExposureLevel? exposureCeiling = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetEventSessionCustomPropertyProjectionsForSessionQuery
            {
                EventSessionId = eventSessionId,
                ExposureCeiling = exposureCeiling
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, SessionProjectionLookupValidationProblem);
    }
}
