using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Instance presentation and deployment shape: branding, domains, admin portal, render policy, and deployment mode.
/// </summary>
/// <remarks>
/// Split out of InstanceSettingsController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/instance/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class InstancePresentationSettingsController : InstanceSettingsControllerBase
{
    private static readonly ApiValidationProblemDescriptor DeploymentModeValidationProblem = new(
        "instanceDeploymentMode",
        "Instance deployment mode validation failed",
        "Deployment mode update failed.");
    private readonly IMediator _mediator;
    private readonly IDeploymentModeProvider _deploymentModeProvider;

    public InstancePresentationSettingsController(
        IMediator mediator,
        IDeploymentModeProvider deploymentModeProvider,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
        _deploymentModeProvider = deploymentModeProvider;
    }

    [HttpGet("branding", Name = RouteNames.GetInstanceBrandingSettings)]
    [EndpointSummary("Get Branding Settings")]
    [EndpointDescription("Returns instance branding and identity settings.")]
    [ProducesResponseType(typeof(BrandingSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BrandingSettingsDto>> GetBrandingSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.Branding);
    }

    [HttpPatch("branding", Name = RouteNames.UpdateInstanceBrandingSettings)]
    [EndpointSummary("Update Branding Settings")]
    [EndpointDescription("Updates instance branding settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateBrandingSettings(
        [FromBody] PatchBrandingSettingsDto settings,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateBrandingSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        if (response.IsSuccess)
        {
            await cacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        }

        return HandleCommandResponse(response);
    }

    [HttpGet("domains", Name = RouteNames.GetInstanceDomainSettings)]
    [EndpointSummary("Get Domain Settings")]
    [EndpointDescription("Returns instance domain and auth provider settings.")]
    [ProducesResponseType(typeof(DomainSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DomainSettingsDto>> GetDomainSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.Domains);
    }

    [HttpPatch("domains", Name = RouteNames.UpdateInstanceDomainSettings)]
    [EndpointSummary("Update Domain Settings")]
    [EndpointDescription("Updates instance domain settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateDomainSettings(
        [FromBody] PatchDomainSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateDomainSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("admin-portal", Name = RouteNames.GetInstanceAdminPortalSettings)]
    [EndpointSummary("Get Admin Portal Settings")]
    [EndpointDescription("Returns dedicated Control Plane Admin Portal enablement and tenant access settings.")]
    [ProducesResponseType(typeof(AdminPortalSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminPortalSettingsDto>> GetAdminPortalSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.AdminPortal);
    }

    [HttpPatch("admin-portal", Name = RouteNames.UpdateInstanceAdminPortalSettings)]
    [EndpointSummary("Update Admin Portal Settings")]
    [EndpointDescription("Updates dedicated Control Plane Admin Portal settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAdminPortalSettings(
        [FromBody] PatchAdminPortalSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateAdminPortalSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("render-policy", Name = RouteNames.GetInstanceRenderPolicySettings)]
    [EndpointSummary("Get Render Policy Settings")]
    [EndpointDescription("Returns instance render policy and UI mode settings.")]
    [ProducesResponseType(typeof(RenderPolicySettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RenderPolicySettingsDto>> GetRenderPolicySettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.RenderPolicy);
    }

    [HttpPatch("render-policy", Name = RouteNames.UpdateInstanceRenderPolicySettings)]
    [EndpointSummary("Update Render Policy Settings")]
    [EndpointDescription("Updates instance render policy settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRenderPolicySettings(
        [FromBody] PatchRenderPolicySettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateRenderPolicySettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("deployment-mode", Name = RouteNames.GetInstanceDeploymentMode)]
    [EndpointSummary("Get Deployment Mode")]
    [EndpointDescription("Returns the current instance deployment mode.")]
    [ProducesResponseType(typeof(DeploymentModeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DeploymentModeDto>> GetDeploymentMode(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var mode = await _deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        return Ok(new DeploymentModeDto { Mode = mode });
    }

    [HttpPost("deployment-mode", Name = RouteNames.UpdateInstanceDeploymentMode)]
    [EndpointSummary("Deployment Mode Is Operator-Controlled")]
    [EndpointDescription("Runtime deployment mode switching is disabled. Set DEPLOYMENT_MODE before first-run onboarding.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateDeploymentMode(
        [FromBody] UpdateDeploymentModeRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        _ = request;
        return this.ToValidationProblem(
            DeploymentModeValidationProblem,
            "Set DEPLOYMENT_MODE before first-run onboarding. Runtime admin switching is disabled.",
            "DeploymentModeChangeRequiresOperatorConfiguration");
    }
}
