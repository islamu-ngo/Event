using System.Security.Claims;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Features.Management.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/management")]
[ApiController]
[EndpointClassification(EndpointClass.Admin)]
public sealed class ManagementController(
    IMediator mediator,
    IAdminContext adminContext) : ControllerBase
{
    [HttpGet("capabilities", Name = RouteNames.GetManagementCapabilities)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get managed-mode capabilities")]
    [EndpointDescription("Returns the bounded Event management contract only when optional managed mode is enabled.")]
    [ProducesResponseType(typeof(ManagementCapabilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagementCapabilitiesDto>> GetCapabilities(
        CancellationToken cancellationToken = default)
    {
        var capabilities = await mediator.Send(new GetManagementCapabilitiesQuery(), cancellationToken);
        if (!capabilities.ManagedModeEnabled)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Managed mode is disabled",
                Detail = "This Event instance is operating independently without a managed Control Plane contract.",
                Instance = Request.Path,
                Extensions = { ["code"] = "managed_mode_disabled" }
            });
        }

        return Ok(capabilities);
    }

    [HttpPost("registration", Name = RouteNames.TriggerManagementRegistration)]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Trigger managed registration")]
    [EndpointDescription("Retries the same durable Event-to-Control-Plane registration attempt for an instance administrator.")]
    [ProducesResponseType(typeof(TriggerManagedRegistrationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TriggerManagedRegistrationResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TriggerManagedRegistrationResultDto>> TriggerRegistration(
        CancellationToken cancellationToken = default)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Instance administrator authority is required to trigger managed registration.");
        }

        var result = await mediator.Send(
            new TriggerManagedControlPlaneRegistrationCommand(),
            cancellationToken);
        return result.Success ? Ok(result) : Accepted(result);
    }

    [HttpGet("instance", Name = RouteNames.GetManagedEventInstance)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Get managed Event instance")]
    [EndpointDescription("Returns Event-owned instance identity, deployment mode, version, and trust freshness.")]
    [ProducesResponseType(typeof(ManagedEventInstanceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedEventInstanceStatusDto>> GetInstance(
        CancellationToken cancellationToken = default)
    {
        var status = await mediator.Send(new GetManagedEventInstanceStatusQuery(), cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpGet("version", Name = RouteNames.GetManagementVersion)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Get Event management version")]
    [ProducesResponseType(typeof(ManagementVersionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagementVersionDto>> GetVersion(
        CancellationToken cancellationToken = default)
    {
        var capabilities = await mediator.Send(new GetManagementCapabilitiesQuery(), cancellationToken);
        return Ok(new ManagementVersionDto(capabilities.EventVersion, capabilities.ManagementApiVersion));
    }

    [HttpGet("health", Name = RouteNames.GetManagementHealth)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Get aggregate Event health")]
    [ProducesResponseType(typeof(ManagementHealthDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagementHealthDto>> GetHealth(
        CancellationToken cancellationToken = default)
    {
        var health = await mediator.Send(new GetManagementHealthQuery(), cancellationToken);
        return Ok(health);
    }

    [HttpPost("upgrade/preflight", Name = RouteNames.EvaluateManagementUpgradePreflight)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Evaluate Event upgrade preflight")]
    [EndpointDescription("Returns bounded blockers for an externally executed Event upgrade without mutating Event state.")]
    [ProducesResponseType(typeof(ManagementUpgradePreflightDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ManagementUpgradePreflightDto>> EvaluateUpgradePreflight(
        [FromBody] ManagementUpgradePreflightRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetManagementUpgradePreflightQuery(
                request.TargetEventVersion,
                request.TargetManagementApiVersion),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("upgrade/postflight", Name = RouteNames.VerifyManagementUpgradePostflight)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Verify Event upgrade postflight")]
    [EndpointDescription("Verifies bounded runtime, contract, registration, mode, and health evidence after an external upgrade.")]
    [ProducesResponseType(typeof(ManagementUpgradePostflightDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ManagementUpgradePostflightDto>> VerifyUpgradePostflight(
        [FromBody] ManagementUpgradePostflightRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetManagementUpgradePostflightQuery(
                request.ExpectedEventVersion,
                request.ExpectedManagementApiVersion),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("tenants/preflight", Name = RouteNames.EvaluateManagedTenantProvisioningPreflight)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Evaluate managed tenant provisioning preflight")]
    [EndpointDescription("Returns a provisional Event-owned policy assessment without creating operations or mutating tenant state.")]
    [ProducesResponseType(typeof(ManagementTenantProvisioningPreflightDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<ManagementTenantProvisioningPreflightDto>> EvaluateTenantProvisioningPreflight(
        [FromBody] ManagementTenantProvisioningRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetManagedInstanceId(out Guid managedInstanceId))
        {
            return Unauthorized(Problem(
                StatusCodes.Status401Unauthorized,
                "managed_principal_invalid",
                "Managed principal is invalid."));
        }

        ManagementTenantProvisioningPreflightDto result = await mediator.Send(
            new GetManagedTenantProvisioningPreflightQuery(managedInstanceId, request),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("tenants/provision", Name = RouteNames.ScheduleManagedTenantProvisioning)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Write)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Schedule managed tenant provisioning")]
    [EndpointDescription("Validates and durably schedules Event-owned tenant creation without calling infrastructure providers.")]
    [ProducesResponseType(typeof(ManagementTenantProvisioningOperationDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ManagementTenantProvisioningOperationDto>> ScheduleTenantProvisioning(
        [FromBody] ManagementTenantProvisioningRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetManagedInstanceId(out Guid managedInstanceId))
        {
            return Unauthorized(Problem(
                StatusCodes.Status401Unauthorized,
                "managed_principal_invalid",
                "Managed principal is invalid."));
        }

        var result = await mediator.Send(
            new ScheduleManagedTenantProvisioningCommand(managedInstanceId, request),
            cancellationToken);
        if (!result.IsSuccess || result.Id is null)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                result.FailureCode ?? "tenant_provisioning_rejected",
                result.Message ?? "Managed tenant provisioning was rejected."));
        }

        return AcceptedAtRoute(
            RouteNames.GetManagedTenantProvisioningOperation,
            new { operationId = result.Id.OperationId },
            result.Id);
    }

    [HttpGet("tenant-provisioning/{operationId:guid}", Name = RouteNames.GetManagedTenantProvisioningOperation)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Read)]
    [EndpointSummary("Get managed tenant provisioning status")]
    [EndpointDescription("Returns safe Event-owned operation state and result references for the authenticated managed instance.")]
    [ProducesResponseType(typeof(ManagementTenantProvisioningOperationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagementTenantProvisioningOperationDto>> GetTenantProvisioningOperation(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetManagedInstanceId(out Guid managedInstanceId))
        {
            return Unauthorized(Problem(
                StatusCodes.Status401Unauthorized,
                "managed_principal_invalid",
                "Managed principal is invalid."));
        }

        ManagementTenantProvisioningOperationDto? operation = await mediator.Send(
            new GetManagedTenantProvisioningOperationQuery(managedInstanceId, operationId),
            cancellationToken);
        return operation is null
            ? NotFound(Problem(
                StatusCodes.Status404NotFound,
                "tenant_provisioning_operation_not_found",
                "Tenant provisioning operation was not found."))
            : Ok(operation);
    }

    [HttpPost(
        "tenant-provisioning/{operationId:guid}/cancel",
        Name = RouteNames.CancelManagedTenantProvisioningOperation)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Write)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Cancel managed tenant provisioning")]
    [EndpointDescription("Cancels a pending operation before any tenant mutation begins.")]
    [ProducesResponseType(typeof(ManagementTenantProvisioningOperationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ManagementTenantProvisioningOperationDto>> CancelTenantProvisioningOperation(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetManagedInstanceId(out Guid managedInstanceId))
        {
            return Unauthorized(Problem(
                StatusCodes.Status401Unauthorized,
                "managed_principal_invalid",
                "Managed principal is invalid."));
        }

        var result = await mediator.Send(
            new CancelManagedTenantProvisioningOperationCommand(managedInstanceId, operationId),
            cancellationToken);
        if (result.IsSuccess && result.Id is not null)
        {
            return Ok(result.Id);
        }

        ProblemDetails problem = Problem(
            result.FailureCode == "tenant_provisioning_operation_not_found"
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict,
            result.FailureCode ?? "tenant_provisioning_cancellation_conflict",
            result.Message ?? "Managed tenant provisioning could not be cancelled.");
        return problem.Status == StatusCodes.Status404NotFound
            ? NotFound(problem)
            : Conflict(problem);
    }

    [HttpPost("credentials/rotate", Name = RouteNames.RotateManagedControlPlaneCredential)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Write)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Rotate Control Plane credential")]
    [EndpointDescription("Replaces the inbound Control Plane key id and hash without receiving its raw secret.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RotateCredential(
        [FromBody] RotateManagedControlPlaneCredentialRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var rotated = await mediator.Send(
            new RotateManagedControlPlaneCredentialCommand(request),
            cancellationToken);
        return rotated ? NoContent() : BadRequest();
    }

    [HttpDelete("credentials", Name = RouteNames.RevokeManagedControlPlaneRegistration)]
    [Authorize(Policy = ManagedControlPlaneAuthorizationPolicies.Write)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Revoke managed Control Plane trust")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RevokeCredential(CancellationToken cancellationToken = default)
    {
        var revoked = await mediator.Send(
            new RevokeManagedControlPlaneRegistrationCommand(),
            cancellationToken);
        return revoked ? NoContent() : Conflict();
    }

    private bool TryGetManagedInstanceId(out Guid managedInstanceId) =>
        Guid.TryParse(
            User.FindFirstValue(ManagedControlPlaneAuthenticationDefaults.ManagedInstanceIdClaim),
            out managedInstanceId)
        && managedInstanceId != Guid.Empty;

    private ProblemDetails Problem(int status, string code, string detail) => new()
    {
        Status = status,
        Title = status == StatusCodes.Status404NotFound
            ? "Resource not found"
            : status == StatusCodes.Status401Unauthorized
                ? "Authentication required"
                : "Managed tenant provisioning rejected",
        Detail = detail,
        Instance = Request.Path,
        Extensions = { ["code"] = code }
    };
}
