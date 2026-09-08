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
/// Instance authentication provider configuration and Keycloak realm operations.
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
public sealed class InstanceAuthenticationSettingsController : InstanceSettingsControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAuthProviderConfigurationService _authProviderConfigurationService;

    public InstanceAuthenticationSettingsController(
        IMediator mediator,
        IAuthProviderConfigurationService authProviderConfigurationService,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
        _authProviderConfigurationService = authProviderConfigurationService;
    }

    [HttpGet("auth-provider", Name = RouteNames.GetInstanceAuthProviderConfiguration)]
    [EndpointSummary("Get Auth Provider Configuration")]
    [EndpointDescription("Returns current auth provider configuration. Secrets are redacted.")]
    [ProducesResponseType(typeof(AuthProviderConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthProviderConfigurationDto>> GetAuthProviderConfiguration(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var configuration = await _mediator.Send(new GetAuthProviderConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [HttpPatch("auth-provider", Name = RouteNames.UpdateInstanceAuthProviderConfiguration)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Update Auth Provider Configuration")]
    [EndpointDescription("Updates auth provider configuration during active setup-secret bootstrap or by an instance administrator. Instance-admin updates block self-lockout.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAuthProviderConfiguration(
        [FromBody] PatchAuthProviderConfigurationDto configuration, CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response;
        if (IsSetupSecretAuthenticated())
        {
            response = await _mediator.Send(
                new UpdateAuthProviderConfigurationDuringSetupCommand { Patch = configuration },
                cancellationToken);
        }
        else
        {
            var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
            if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

            response = await _mediator.Send(
                new UpdateAuthProviderConfigurationCommand { UserId = userId.Value, Patch = configuration },
                cancellationToken);
        }

        return HandleCommandResponse(response);
    }

    [HttpPost("auth-provider/keycloak/doctor", Name = RouteNames.RunInstanceKeycloakRealmDoctor)]
    [EndpointSummary("Run Keycloak Realm Doctor")]
    [EndpointDescription("Runs read-only Keycloak realm diagnostics. Temporary admin credentials are used only for this request and are not stored.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(KeycloakRealmDoctorResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<KeycloakRealmDoctorResultDto>> RunKeycloakRealmDoctor(
        [FromBody] KeycloakRealmDoctorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var result = await _mediator.Send(new RunKeycloakRealmDoctorQuery { Request = request }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("auth-provider/keycloak/sync-preview", Name = RouteNames.PreviewInstanceKeycloakRealmSync)]
    [EndpointSummary("Preview Keycloak Realm Sync")]
    [EndpointDescription("Generates a read-only additive Keycloak realm sync plan. Temporary admin credentials are used only for this request and are not stored.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(KeycloakRealmSyncPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<KeycloakRealmSyncPlanDto>> PreviewKeycloakRealmSync(
        [FromBody] KeycloakRealmSyncPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var result = await _mediator.Send(new PreviewKeycloakRealmSyncQuery { Request = request }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("auth-provider/keycloak/sync-apply", Name = RouteNames.ApplyInstanceKeycloakRealmSync)]
    [EndpointSummary("Apply Keycloak Realm Sync")]
    [EndpointDescription("Applies backup-confirmed additive Keycloak realm repairs. Temporary admin credentials are used only for this request and are not stored.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(KeycloakRealmSyncPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<KeycloakRealmSyncPlanDto>> ApplyKeycloakRealmSync(
        [FromBody] KeycloakRealmSyncApplyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var result = await _mediator.Send(new ApplyKeycloakRealmSyncCommand { Request = request }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("auth-provider/keycloak/client-secret/rotate", Name = RouteNames.RotateInstanceKeycloakClientSecret)]
    [EndpointSummary("Rotate Keycloak Client Secret")]
    [EndpointDescription("Rotates an application-managed Keycloak client secret. Deployment-managed secrets return operator instructions and are not changed by the application.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(KeycloakClientSecretRotationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<KeycloakClientSecretRotationResultDto>> RotateKeycloakClientSecret(
        [FromBody] KeycloakClientSecretRotationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        if (!await AdminContext.IsInstanceAdminAsync(userId.Value, cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator authority is required for this operation.");

        var result = await _mediator.Send(
            new RotateKeycloakClientSecretCommand { UserId = userId.Value, Request = request },
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("auth-provider/status", Name = RouteNames.GetInstanceAuthProviderConfigurationStatus)]
    [AllowAnonymous]
    [EndpointSummary("Check Auth Provider Configuration Status")]
    [EndpointDescription("Returns whether any auth provider has been configured.")]
    [ProducesResponseType(typeof(ProviderConfigurationStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProviderConfigurationStatusDto>> IsAuthProviderConfigured(CancellationToken cancellationToken = default)
    {
        var isConfigured = await _authProviderConfigurationService.IsConfiguredAsync();
        return Ok(new ProviderConfigurationStatusDto(isConfigured));
    }
}
