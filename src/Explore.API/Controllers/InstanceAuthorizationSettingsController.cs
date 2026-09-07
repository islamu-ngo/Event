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
/// Instance authorization provider configuration and policy package distribution.
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
public sealed class InstanceAuthorizationSettingsController : InstanceSettingsControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAuthorizationProviderConfigurationService _authorizationProviderConfigurationService;

    public InstanceAuthorizationSettingsController(
        IMediator mediator,
        IAuthorizationProviderConfigurationService authorizationProviderConfigurationService,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
        _authorizationProviderConfigurationService = authorizationProviderConfigurationService;
    }

    [HttpGet("authz-provider", Name = RouteNames.GetInstanceAuthorizationProviderConfiguration)]
    [EndpointSummary("Get Authorization Provider Configuration")]
    [EndpointDescription("Returns current authorization provider configuration for instance administration.")]
    [ProducesResponseType(typeof(AuthorizationProviderConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthorizationProviderConfigurationDto>> GetAuthorizationProviderConfiguration(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var configuration = await _mediator.Send(new GetAuthorizationProviderConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [HttpPatch("authz-provider", Name = RouteNames.UpdateInstanceAuthorizationProviderConfiguration)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Update Authorization Provider Configuration")]
    [EndpointDescription("Updates authorization provider configuration during active setup-secret bootstrap or by an instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAuthorizationProviderConfiguration(
        [FromBody] PatchAuthorizationProviderConfigurationDto configuration, CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response;
        if (IsSetupSecretAuthenticated())
        {
            response = await _mediator.Send(
                new UpdateAuthorizationProviderConfigurationDuringSetupCommand { Patch = configuration },
                cancellationToken);
        }
        else
        {
            var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
            if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

            response = await _mediator.Send(
                new UpdateAuthorizationProviderConfigurationCommand { UserId = userId.Value, Patch = configuration },
                cancellationToken);
        }

        return HandleCommandResponse(response);
    }

    [HttpPost("authz-provider/sync", Name = RouteNames.SyncInstanceAuthorizationPolicyPackage)]
    [EndpointSummary("Sync Authorization Policy Package")]
    [EndpointDescription("Publishes the authorization policy package. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SyncAuthorizationPolicyPackage(
        [FromBody] AuthorizationPolicyPackageSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var response = await _mediator.Send(
            new SyncAuthorizationPolicyPackageCommand { Request = request },
            cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpGet("authz-provider/package", Name = RouteNames.DownloadInstanceAuthorizationPolicyPackage)]
    [EndpointSummary("Download Authorization Policy Package")]
    [EndpointDescription("Downloads a ZIP archive containing the authorization policy package and manual cerbosctl instructions. Requires instance administrator.")]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DownloadAuthorizationPolicyPackage(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        try
        {
            var archive = await _mediator.Send(new DownloadAuthorizationPolicyPackageQuery(), cancellationToken);
            return File(archive.Content.ToArray(), archive.ContentType, archive.FileName);
        }
        catch (PolicyPackageUnavailableException)
        {
            return AuthorizationPolicyPackageUnavailableProblem();
        }
    }

    /// <summary>
    /// Operator diagnostics for whether the PDP is serving the policy package this deployment published.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the anonymous <c>authz-provider/status</c> readiness probe below. That
    /// one answers "can onboarding proceed?" for an unauthenticated caller; this one reports the observed
    /// policy revision and store health, which tells an attacker whether authorization is currently
    /// degraded. It is gated on instance administration for that reason.
    /// </remarks>
    [HttpGet("authz-provider/package/status", Name = RouteNames.GetInstanceAuthorizationPolicyPackageStatus)]
    [EndpointSummary("Get Authorization Policy Package Status")]
    [EndpointDescription("Reports authorization policy package health, the observed policy store revision, and the recovery action. Requires instance administrator.")]
    [ProducesResponseType(typeof(AuthorizationPolicyPackageStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthorizationPolicyPackageStatusDto>> GetAuthorizationPolicyPackageStatus(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var status = await _mediator.Send(new GetAuthorizationPolicyPackageStatusQuery(), cancellationToken);
        return Ok(status);
    }

    [HttpGet("authz-provider/status", Name = RouteNames.GetInstanceAuthorizationProviderConfigurationStatus)]
    [AllowAnonymous]
    [EndpointSummary("Check Authorization Provider Configuration Status")]
    [EndpointDescription("Returns authorization readiness plus deployment ownership and bootstrap state for onboarding routing.")]
    [ProducesResponseType(typeof(ProviderConfigurationStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProviderConfigurationStatusDto>> IsAuthorizationProviderConfigured(CancellationToken cancellationToken = default)
    {
        var configuration = await _authorizationProviderConfigurationService.ReadConfigurationAsync();
        return Ok(new ProviderConfigurationStatusDto(
            configuration.AuthorizationProviderConfigured,
            configuration.AuthorizationProviderManagedByDeployment,
            configuration.AuthorizationProviderBootstrapStatus));
    }

    private ActionResult AuthorizationPolicyPackageUnavailableProblem() =>
        this.ToServiceUnavailableProblem(
            "Authorization policy package unavailable",
            "The bundled Cerbos policy package is not available to this API deployment. Mount or bundle the package directory and retry the download.",
            ApiProblemCodes.AuthorizationPolicyPackageUnavailable);
}
