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
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Per-tenant instance administration configuration: effective settings, setting locks, and plan assignment transitions.
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
public sealed class InstanceTenantConfigurationController : EventControllerBase
{
    private readonly IQueryHandler<GetInstanceTenantPlanAssignmentQuery, InstanceTenantPlanAssignmentDto?> _assignmentQuery;
    private readonly IQueryHandler<GetInstanceTenantEffectiveConfigurationQuery, InstanceTenantEffectiveConfigurationDto> _configurationQuery;
    private readonly ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>> _setSetting;
    private readonly ICommandHandler<LockInstanceTenantSettingCommand, BaseCommandResponse<Guid>> _lockSetting;
    private readonly ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>> _unlockSetting;
    private readonly ICommandHandler<SwitchInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _switchAssignment;
    private readonly ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _applyAssignment;
    private readonly ICommandHandler<RollbackInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> _rollbackAssignment;
    private readonly IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> _identityQuery;
    private readonly IResourceAssembler<InstanceTenantEffectiveConfigurationDto, InstanceTenantEffectiveConfigurationDto> _tenantEffectiveConfigurationAssembler;

    public InstanceTenantConfigurationController(
        IQueryHandler<GetInstanceTenantPlanAssignmentQuery, InstanceTenantPlanAssignmentDto?> assignmentQuery,
        IQueryHandler<GetInstanceTenantEffectiveConfigurationQuery, InstanceTenantEffectiveConfigurationDto> configurationQuery,
        ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>> setSetting,
        ICommandHandler<LockInstanceTenantSettingCommand, BaseCommandResponse<Guid>> lockSetting,
        ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>> unlockSetting,
        ICommandHandler<SwitchInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> switchAssignment,
        ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> applyAssignment,
        ICommandHandler<RollbackInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>> rollbackAssignment,
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        IResourceAssembler<InstanceTenantEffectiveConfigurationDto, InstanceTenantEffectiveConfigurationDto> tenantEffectiveConfigurationAssembler)
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

    [HttpGet("tenants/{tenantId:guid}/plan-assignment", Name = RouteNames.GetInstanceAdminTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Tenant Plan Assignment")]
    [EndpointDescription("Returns the active tenant plan assignment for one tenant.")]
    [ProducesResponseType(typeof(InstanceTenantPlanAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InstanceTenantPlanAssignmentDto>> GetTenantPlanAssignment(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentQuery.QueryAsync(new GetInstanceTenantPlanAssignmentQuery(tenantId), cancellationToken);

        return assignment is null ? NotFound() : Ok(assignment);
    }

    [HttpGet("tenants/{tenantId:guid}/effective-configuration", Name = RouteNames.GetInstanceAdminTenantEffectiveConfiguration)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Tenant Effective Configuration")]
    [EndpointDescription("Returns resolved settings, active plan assignment, and quota usage for one tenant.")]
    [ProducesResponseType(typeof(HalResource<InstanceTenantEffectiveConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceTenantEffectiveConfigurationDto>>> GetTenantEffectiveConfiguration(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationQuery.QueryAsync(
            new GetInstanceTenantEffectiveConfigurationQuery(tenantId),
            cancellationToken);
        var resource = await _tenantEffectiveConfigurationAssembler.ToResource(configuration, HttpContext);

        return Ok(resource);
    }

    [HttpPut("tenants/{tenantId:guid}/settings/{key}", Name = RouteNames.SetInstanceAdminTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Set Instance Administration Tenant Setting")]
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
        [FromBody] SetInstanceTenantSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _setSetting.ExecuteAsync(
            new SetInstanceTenantSettingCommand(tenantId, key, request.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/settings/{key}/lock", Name = RouteNames.LockInstanceAdminTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Lock Instance Administration Tenant Setting")]
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
            new LockInstanceTenantSettingCommand(tenantId, key),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpDelete("tenants/{tenantId:guid}/settings/{key}/lock", Name = RouteNames.UnlockInstanceAdminTenantSetting)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Unlock Instance Administration Tenant Setting")]
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
            new UnlockInstanceTenantSettingCommand(tenantId, key),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignment", Name = RouteNames.SwitchInstanceAdminTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Switch Instance Administration Tenant Plan Assignment")]
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
            new SwitchInstanceTenantPlanAssignmentCommand(tenantId, request.TenantPlanVersionId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignments/{assignmentId:guid}/apply", Name = RouteNames.ApplyInstanceAdminTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Apply Instance Administration Tenant Plan Assignment")]
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
            new ApplyInstanceTenantPlanAssignmentCommand(tenantId, assignmentId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/plan-assignments/{assignmentId:guid}/rollback", Name = RouteNames.RollbackInstanceAdminTenantPlanAssignment)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Rollback Instance Administration Tenant Plan Assignment")]
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
            new RollbackInstanceTenantPlanAssignmentCommand(tenantId, assignmentId, operatorId.Value),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
