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
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
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
/// Tenant lifecycle from instance administration: creation, activation, suspension, archive, reactivation, and purge scheduling.
/// </summary>
/// <remarks>
/// Split out of InstanceAdminController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/admin/instance")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstanceTenantLifecycleController : EventControllerBase
{
    private readonly ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>> _createTenant;
    private readonly IQueryHandler<GetInstanceTenantDetailsQuery, InstanceTenantDetailDto?> _tenantQuery;
    private readonly ICommandHandler<TransitionInstanceTenantLifecycleCommand, BaseCommandResponse<InstanceTenantLifecycleTransitionDto>> _transitionTenant;
    private readonly IResourceAssembler<InstanceTenantDetailDto, InstanceTenantListItemDto> _tenantAssembler;
    private readonly IDeploymentModeProvider _deploymentMode;

    public InstanceTenantLifecycleController(
        ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>> createTenant,
        IQueryHandler<GetInstanceTenantDetailsQuery, InstanceTenantDetailDto?> tenantQuery,
        ICommandHandler<TransitionInstanceTenantLifecycleCommand, BaseCommandResponse<InstanceTenantLifecycleTransitionDto>> transitionTenant,
        IResourceAssembler<InstanceTenantDetailDto, InstanceTenantListItemDto> tenantAssembler,
        IDeploymentModeProvider deploymentMode)
    {
        _createTenant = createTenant;
        _tenantQuery = tenantQuery;
        _transitionTenant = transitionTenant;
        _tenantAssembler = tenantAssembler;
        _deploymentMode = deploymentMode;
    }

    [HttpGet("tenants/{tenantId:guid}", Name = RouteNames.GetInstanceAdminTenantById)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Tenant")]
    [EndpointDescription("Returns an exact tenant lifecycle detail for instance administrators; SingleTenant mode accepts only the fixed default tenant.")]
    [ProducesResponseType(typeof(HalResource<InstanceTenantDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<InstanceTenantDetailDto>>> GetTenantById(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        DeploymentMode mode = await _deploymentMode.GetCurrentModeAsync(cancellationToken);
        if (!AllowsExactTarget(mode, tenantId))
            return NotFound();

        var tenant = await _tenantQuery.QueryAsync(new GetInstanceTenantDetailsQuery(tenantId), cancellationToken);
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

    [HttpPost("tenants", Name = RouteNames.CreateInstanceAdminTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Create Instance Administration Tenant")]
    [EndpointDescription("Creates a tenant through multi-tenant instance administration.")]
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

    [HttpPost("tenants/{tenantId:guid}/activate", Name = RouteNames.ActivateInstanceAdminTenant)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Activate Instance Administration Tenant")]
    [EndpointDescription("Explicitly activates a tenant after current identity and capacity checks; SingleTenant mode accepts only the fixed default tenant.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> ActivateTenant(
        Guid tenantId,
        [FromBody] InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Active, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/suspend", Name = RouteNames.SuspendInstanceAdminTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Suspend Instance Administration Tenant")]
    [EndpointDescription("Suspends a tenant through multi-tenant instance administration.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> SuspendTenant(
        Guid tenantId,
        [FromBody] InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Suspended, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/archive", Name = RouteNames.ArchiveInstanceAdminTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Archive Instance Administration Tenant")]
    [EndpointDescription("Archives a tenant through multi-tenant instance administration.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> ArchiveTenant(
        Guid tenantId,
        [FromBody] InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Archived, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/reactivate", Name = RouteNames.ReactivateInstanceAdminTenant)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Reactivate Instance Administration Tenant")]
    [EndpointDescription("Reactivates a suspended or archived tenant through multi-tenant instance administration.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> ReactivateTenant(
        Guid tenantId,
        [FromBody] InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Active, dto, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/schedule-purge", Name = RouteNames.ScheduleInstanceAdminTenantPurge)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Schedule Instance Administration Tenant Purge")]
    [EndpointDescription("Records audited tenant purge intent through multi-tenant instance administration without deleting data in the request path.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceTenantLifecycleTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> ScheduleTenantPurge(
        Guid tenantId,
        [FromBody] InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken = default) =>
        TransitionTenant(tenantId, TenantStatusEnum.Purged, dto, cancellationToken);

    private static bool AllowsExactTarget(DeploymentMode mode, Guid tenantId) =>
        tenantId != Guid.Empty && (mode == DeploymentMode.MultiTenant
            || mode == DeploymentMode.SingleTenant && tenantId == PlatformDefaults.DefaultTenantId);

    private async Task<ActionResult<BaseCommandResponse<InstanceTenantLifecycleTransitionDto>>> TransitionTenant(
        Guid tenantId,
        TenantStatusEnum status,
        InstanceTenantLifecycleTransitionRequestDto? dto,
        CancellationToken cancellationToken)
    {
        DeploymentMode mode = await _deploymentMode.GetCurrentModeAsync(cancellationToken);
        if (!AllowsExactTarget(mode, tenantId)
            || mode == DeploymentMode.SingleTenant && status != TenantStatusEnum.Active)
            return NotFound();

        var response = await _transitionTenant.ExecuteAsync(
            new TransitionInstanceTenantLifecycleCommand(tenantId, status, dto?.Reason, dto?.ConfirmationText),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
