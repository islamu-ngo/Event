using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Per-tenant control-plane configuration: effective settings, setting locks, and plan assignment transitions.
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
public sealed class ControlPlaneTenantConfigurationController : EventControllerBase
{
    private readonly IQueryHandler<GetControlPlaneTenantPlanAssignmentQuery, ControlPlaneTenantPlanAssignmentDto?> _assignmentQuery;
    private readonly IQueryHandler<GetControlPlaneTenantEffectiveConfigurationQuery, ControlPlaneTenantEffectiveConfigurationDto> _configurationQuery;
    private readonly ICommandHandler<SetControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> _setSetting;
    private readonly ICommandHandler<LockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> _lockSetting;
    private readonly ICommandHandler<UnlockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> _unlockSetting;
    private readonly ICommandHandler<SwitchControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _switchAssignment;
    private readonly ICommandHandler<ApplyControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _applyAssignment;
    private readonly ICommandHandler<RollbackControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _rollbackAssignment;
    private readonly IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> _identityQuery;
    private readonly IResourceAssembler<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationDto> _tenantEffectiveConfigurationAssembler;

    public ControlPlaneTenantConfigurationController(
        IQueryHandler<GetControlPlaneTenantPlanAssignmentQuery, ControlPlaneTenantPlanAssignmentDto?> assignmentQuery,
        IQueryHandler<GetControlPlaneTenantEffectiveConfigurationQuery, ControlPlaneTenantEffectiveConfigurationDto> configurationQuery,
        ICommandHandler<SetControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> setSetting,
        ICommandHandler<LockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> lockSetting,
        ICommandHandler<UnlockControlPlaneTenantSettingCommand, BaseCommandResponse<Guid>> unlockSetting,
        ICommandHandler<SwitchControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> switchAssignment,
        ICommandHandler<ApplyControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> applyAssignment,
        ICommandHandler<RollbackControlPlaneTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> rollbackAssignment,
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        IResourceAssembler<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationDto> tenantEffectiveConfigurationAssembler)
    {
        _assignmentQuery = assignmentQuery;
        _configurationQuery = configurationQuery;
        _setSetting = setSetting;
        _lockSetting = lockSetting;
        _unlockSetting = unlockSetting;
        _switchAssignment = switchAssignment;
        _applyAssignment = applyAssignment;
        _rollbackAssignment = rollbackAssignment;
        _identityQuery = identityQuery;
        _tenantEffectiveConfigurationAssembler = tenantEffectiveConfigurationAssembler;
    }

    [HttpGet("tenants/{tenantId:guid}/plan-assignment", Name = RouteNames.GetControlPlaneTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenant Plan Assignment")]
    [EndpointDescription("Returns the active tenant plan assignment for one tenant.")]
    [ProducesResponseType(typeof(ControlPlaneTenantPlanAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ControlPlaneTenantPlanAssignmentDto>> GetTenantPlanAssignment(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentQuery.QueryAsync(new GetControlPlaneTenantPlanAssignmentQuery(tenantId), cancellationToken);

        return assignment is null ? NotFound() : Ok(assignment);
    }

    [HttpGet("tenants/{tenantId:guid}/effective-configuration", Name = RouteNames.GetControlPlaneTenantEffectiveConfiguration)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenant Effective Configuration")]
    [EndpointDescription("Returns resolved settings, active plan assignment, and quota usage for one tenant.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneTenantEffectiveConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ControlPlaneTenantEffectiveConfigurationDto>>> GetTenantEffectiveConfiguration(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationQuery.QueryAsync(
            new GetControlPlaneTenantEffectiveConfigurationQuery(tenantId),
            cancellationToken);
        var resource = await _tenantEffectiveConfigurationAssembler.ToResource(configuration, HttpContext);

        return Ok(resource);
    }

    [HttpPut("tenants/{tenantId:guid}/settings/{key}", Name = RouteNames.SetControlPlaneTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Set Control Plane Tenant Setting")]
    [EndpointDescription("Writes or updates a tenant-scoped setting override for one tenant.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetTenantSetting(
        Guid tenantId,
        string key,
        [FromBody] SetControlPlaneTenantSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _setSetting.ExecuteAsync(
            new SetControlPlaneTenantSettingCommand(tenantId, key, request.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/settings/{key}/lock", Name = RouteNames.LockControlPlaneTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Lock Control Plane Tenant Setting")]
    [EndpointDescription("Locks a tenant setting override so the tenant cannot edit or unlock it.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> LockTenantSetting(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _lockSetting.ExecuteAsync(
            new LockControlPlaneTenantSettingCommand(tenantId, key),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpDelete("tenants/{tenantId:guid}/settings/{key}/lock", Name = RouteNames.UnlockControlPlaneTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Unlock Control Plane Tenant Setting")]
    [EndpointDescription("Unlocks a previously locked tenant setting override so the tenant can edit it again.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UnlockTenantSetting(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _unlockSetting.ExecuteAsync(
            new UnlockControlPlaneTenantSettingCommand(tenantId, key),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignment", Name = RouteNames.SwitchControlPlaneTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Switch Control Plane Tenant Plan Assignment")]
    [EndpointDescription("Switches one tenant to a selected tenant plan version without automatically applying settings.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SwitchTenantPlanAssignment(
        Guid tenantId,
        [FromBody] SwitchTenantPlanAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var operatorId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!operatorId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await _switchAssignment.ExecuteAsync(
            new SwitchControlPlaneTenantPlanAssignmentCommand(tenantId, request.TenantPlanVersionId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignments/{assignmentId:guid}/apply", Name = RouteNames.ApplyControlPlaneTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Apply Control Plane Tenant Plan Assignment")]
    [EndpointDescription("Explicitly applies a tenant plan assignment's settings to the tenant setting store.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ApplyTenantPlanAssignment(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var operatorId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!operatorId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await _applyAssignment.ExecuteAsync(
            new ApplyControlPlaneTenantPlanAssignmentCommand(tenantId, assignmentId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignments/{assignmentId:guid}/rollback", Name = RouteNames.RollbackControlPlaneTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Rollback Control Plane Tenant Plan Assignment")]
    [EndpointDescription("Rolls one tenant back to a previous tenant plan assignment.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RollbackTenantPlanAssignment(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var operatorId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!operatorId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await _rollbackAssignment.ExecuteAsync(
            new RollbackControlPlaneTenantPlanAssignmentCommand(tenantId, assignmentId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
