using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Tenant lifecycle from the control plane: creation, activation, suspension, archive, reactivation, and purge scheduling.
/// </summary>
/// <remarks>
/// Split out of ControlPlaneController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/admin/control-plane")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ControlPlaneTenantLifecycleController : EventControllerBase
{
    private readonly ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>> _createTenant;
    private readonly IQueryHandler<GetControlPlaneTenantDetailsQuery, ControlPlaneTenantDetailDto?> _tenantQuery;
    private readonly ICommandHandler<TransitionControlPlaneTenantLifecycleCommand, BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>> _transitionTenant;
    private readonly IResourceAssembler<ControlPlaneTenantDetailDto, ControlPlaneTenantListItemDto> _tenantAssembler;
    private readonly IDeploymentModeProvider _deploymentMode;

    public ControlPlaneTenantLifecycleController(
        ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>> createTenant,
        IQueryHandler<GetControlPlaneTenantDetailsQuery, ControlPlaneTenantDetailDto?> tenantQuery,
        ICommandHandler<TransitionControlPlaneTenantLifecycleCommand, BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>> transitionTenant,
        IResourceAssembler<ControlPlaneTenantDetailDto, ControlPlaneTenantListItemDto> tenantAssembler,
        IDeploymentModeProvider deploymentMode)
    {
        _createTenant = createTenant;
        _tenantQuery = tenantQuery;
        _transitionTenant = transitionTenant;
        _tenantAssembler = tenantAssembler;
        _deploymentMode = deploymentMode;
    }

    [HttpGet("tenants/{tenantId:guid}", Name = RouteNames.GetControlPlaneTenantById)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenant")]
    [EndpointDescription("Returns an exact tenant lifecycle detail for instance administrators; SingleTenant mode accepts only the fixed default tenant.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneTenantDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<ControlPlaneTenantDetailDto>>> GetTenantById(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        DeploymentMode mode = await _deploymentMode.GetCurrentModeAsync(cancellationToken);
        if (!AllowsExactTarget(mode, tenantId))
            return NotFound();

        var tenant = await _tenantQuery.QueryAsync(new GetControlPlaneTenantDetailsQuery(tenantId), cancellationToken);
        if (tenant is null || !Enum.IsDefined((TenantStatusEnum)tenant.StatusId))
        {
            return NotFound();
        }

        var resource = await _tenantAssembler.ToResource(tenant, HttpContext);
        if (mode == DeploymentMode.SingleTenant)
        {
            foreach (string relation in resource.Links.Keys.Where(relation => relation is not "self" and not "activate").ToArray())
                resource.Links.Remove(relation);
        }
        Response.Headers.CacheControl = "no-store";
        return Ok(resource);
    }

    [HttpPost("tenants", Name = RouteNames.CreateControlPlaneTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Create Control Plane Tenant")]
    [EndpointDescription("Creates a tenant through the multi-tenant control plane.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateTenant(
        [FromBody] CreateTenantDto dto,
        CancellationToken cancellationToken = default)
    {
        var response = await _createTenant.ExecuteAsync(new CreateTenantCommand
        {
            TenantDto = dto,
            RequestingUserId = CurrentUserId
        }, cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/activate", Name = RouteNames.ActivateControlPlaneTenant)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Activate Control Plane Tenant")]
    [EndpointDescription("Explicitly activates a tenant after current identity and capacity checks; SingleTenant mode accepts only the fixed default tenant.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> ActivateTenant(
        Guid tenantId,
        [FromBody] ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Active, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/suspend", Name = RouteNames.SuspendControlPlaneTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Suspend Control Plane Tenant")]
    [EndpointDescription("Suspends a tenant through the multi-tenant control plane.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> SuspendTenant(
        Guid tenantId,
        [FromBody] ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Suspended, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/archive", Name = RouteNames.ArchiveControlPlaneTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Archive Control Plane Tenant")]
    [EndpointDescription("Archives a tenant through the multi-tenant control plane.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> ArchiveTenant(
        Guid tenantId,
        [FromBody] ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Archived, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/reactivate", Name = RouteNames.ReactivateControlPlaneTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Reactivate Control Plane Tenant")]
    [EndpointDescription("Reactivates a suspended or archived tenant through the multi-tenant control plane.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> ReactivateTenant(
        Guid tenantId,
        [FromBody] ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Active, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/schedule-purge", Name = RouteNames.ScheduleControlPlaneTenantPurge)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Schedule Control Plane Tenant Purge")]
    [EndpointDescription("Records audited tenant purge intent through the multi-tenant control plane without deleting data in the request path.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> ScheduleTenantPurge(
        Guid tenantId,
        [FromBody] ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Purged, dto, cancellationToken);

    private static bool AllowsExactTarget(DeploymentMode mode, Guid tenantId) =>
        tenantId != Guid.Empty && (mode == DeploymentMode.MultiTenant
            || mode == DeploymentMode.SingleTenant && tenantId == PlatformDefaults.DefaultTenantId);

    private async Task<ActionResult<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>> TransitionTenant(
        Guid tenantId,
        TenantStatusEnum status,
        ControlPlaneTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken)
    {
        DeploymentMode mode = await _deploymentMode.GetCurrentModeAsync(cancellationToken);
        if (!AllowsExactTarget(mode, tenantId)
            || mode == DeploymentMode.SingleTenant && status != TenantStatusEnum.Active)
            return NotFound();

        var response = await _transitionTenant.ExecuteAsync(
            new TransitionControlPlaneTenantLifecycleCommand(tenantId, status, dto?.Reason, dto?.ConfirmationText),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
