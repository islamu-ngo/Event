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
/// Instance governance policy: modules, event and organization policy, delegation, AI/MCP, analytics, and footer.
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
public sealed class InstanceGovernanceSettingsController : InstanceSettingsControllerBase
{
    private readonly IMediator _mediator;

    public InstanceGovernanceSettingsController(
        IMediator mediator,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
    }

    [HttpGet("modules", Name = RouteNames.GetInstanceModuleSettings)]
    [EndpointSummary("Get Module Settings")]
    [EndpointDescription("Returns instance module enablement flags.")]
    [ProducesResponseType(typeof(ModuleSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ModuleSettingsDto>> GetModuleSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.Modules);
    }

    [HttpPatch("modules", Name = RouteNames.UpdateInstanceModuleSettings)]
    [EndpointSummary("Update Module Settings")]
    [EndpointDescription("Updates instance module enablement flags. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateModuleSettings(
        [FromBody] PatchModuleSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateModuleSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("events", Name = RouteNames.GetInstanceEventPolicy)]
    [EndpointSummary("Get Event Policy")]
    [EndpointDescription("Returns instance event lifecycle policy settings.")]
    [ProducesResponseType(typeof(EventPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EventPolicyDto>> GetEventPolicy(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.EventPolicy);
    }

    [HttpPatch("events", Name = RouteNames.UpdateInstanceEventPolicy)]
    [EndpointSummary("Update Event Policy")]
    [EndpointDescription("Updates instance event lifecycle policy. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateEventPolicy(
        [FromBody] PatchEventPolicyDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateEventPolicyCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("organizations", Name = RouteNames.GetInstanceOrganizationPolicy)]
    [EndpointSummary("Get Organization Policy")]
    [EndpointDescription("Returns instance organization registration policy settings.")]
    [ProducesResponseType(typeof(OrganizationPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OrganizationPolicyDto>> GetOrganizationPolicy(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.OrganizationPolicy);
    }

    [HttpPatch("organizations", Name = RouteNames.UpdateInstanceOrganizationPolicy)]
    [EndpointSummary("Update Organization Policy")]
    [EndpointDescription("Updates instance organization policy. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateOrganizationPolicy(
        [FromBody] PatchOrganizationPolicyDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateOrganizationPolicyCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("tenant-delegation", Name = RouteNames.GetInstanceTenantDelegationSettings)]
    [EndpointSummary("Get Tenant Delegation Settings")]
    [EndpointDescription("Returns instance tenant delegation and override lock settings.")]
    [ProducesResponseType(typeof(TenantDelegationSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TenantDelegationSettingsDto>> GetTenantDelegationSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.TenantDelegation);
    }

    [HttpPatch("tenant-delegation", Name = RouteNames.UpdateInstanceTenantDelegationSettings)]
    [EndpointSummary("Update Tenant Delegation Settings")]
    [EndpointDescription("Updates instance tenant delegation settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateTenantDelegationSettings(
        [FromBody] PatchTenantDelegationSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateTenantDelegationSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("ai-assistant", Name = RouteNames.GetInstanceAiAssistantGovernanceSettings)]
    [EndpointSummary("Get AI Assistant Governance Settings")]
    [EndpointDescription("Returns instance AI assistant defaults and tenant override lock settings.")]
    [ProducesResponseType(typeof(AiAssistantGovernanceSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiAssistantGovernanceSettingsDto>> GetAiAssistantGovernanceSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.AiAssistant);
    }

    [HttpPatch("ai-assistant", Name = RouteNames.UpdateInstanceAiAssistantGovernanceSettings)]
    [EndpointSummary("Update AI Assistant Governance Settings")]
    [EndpointDescription("Updates instance AI assistant defaults and tenant override lock settings. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAiAssistantGovernanceSettings(
        [FromBody] PatchAiAssistantGovernanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateAiAssistantGovernanceSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("mcp", Name = RouteNames.GetInstanceMcpGovernanceSettings)]
    [EndpointSummary("Get MCP Governance Settings")]
    [EndpointDescription("Returns instance MCP runtime enablement and tenant override lock settings. Startup endpoint path and stateless mode are not runtime-editable.")]
    [ProducesResponseType(typeof(McpGovernanceSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<McpGovernanceSettingsDto>> GetMcpGovernanceSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings.Mcp);
    }

    [HttpPatch("mcp", Name = RouteNames.UpdateInstanceMcpGovernanceSettings)]
    [EndpointSummary("Update MCP Governance Settings")]
    [EndpointDescription("Updates instance MCP runtime enablement and tenant override locks. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateMcpGovernanceSettings(
        [FromBody] PatchMcpGovernanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateMcpGovernanceSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("analytics-governance", Name = RouteNames.GetInstanceAnalyticsGovernanceSettings)]
    [EndpointSummary("Get Analytics Governance Settings")]
    [EndpointDescription("Returns analytics and cookie consent governance settings.")]
    [ProducesResponseType(typeof(AnalyticsGovernanceSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnalyticsGovernanceSettingsDto>> GetAnalyticsGovernanceSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetAnalyticsGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings);
    }

    [HttpPatch("analytics-governance", Name = RouteNames.UpdateInstanceAnalyticsGovernanceSettings)]
    [EndpointSummary("Update Analytics Governance Settings")]
    [EndpointDescription("Updates analytics and cookie consent governance settings. Requires instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAnalyticsGovernanceSettings(
        [FromBody] PatchAnalyticsGovernanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateAnalyticsGovernanceSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("footer-governance", Name = RouteNames.GetFooterGovernanceSettings)]
    [EndpointSummary("Get Footer Governance Settings")]
    [EndpointDescription("Returns instance-level footer lock flags. Requires instance administrator.")]
    [ProducesResponseType(typeof(FooterGovernanceSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FooterGovernanceSettingsDto>> GetFooterGovernanceSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetFooterGovernanceSettingsQuery(), cancellationToken);
        return Ok(settings);
    }

    [HttpPatch("footer-governance", Name = RouteNames.UpdateFooterGovernanceSettings)]
    [EndpointSummary("Update Footer Governance Settings")]
    [EndpointDescription("Updates instance-level footer lock flags. Requires instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateFooterGovernanceSettings(
        [FromBody] PatchFooterGovernanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateFooterGovernanceSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }
}
