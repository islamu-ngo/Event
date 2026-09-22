using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
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
    private readonly IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> _identityQuery;
    private readonly IAuthProviderConfigurationService _authProviderConfigurationService;
    private readonly IQueryHandler<GetAuthProviderConfigurationQuery, AuthProviderConfigurationDto> _getAuthProviderConfigHandler;
    private readonly ICommandHandler<UpdateAuthProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>> _updateAuthConfigDuringSetupHandler;
    private readonly ICommandHandler<UpdateAuthProviderConfigurationCommand, BaseCommandResponse<Guid>> _updateAuthConfigHandler;

    public InstanceAuthenticationSettingsController(
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        IAuthProviderConfigurationService authProviderConfigurationService,
        IQueryHandler<GetAuthProviderConfigurationQuery, AuthProviderConfigurationDto> getAuthProviderConfigHandler,
        ICommandHandler<UpdateAuthProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>> updateAuthConfigDuringSetupHandler,
        ICommandHandler<UpdateAuthProviderConfigurationCommand, BaseCommandResponse<Guid>> updateAuthConfigHandler,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _identityQuery = identityQuery;
        _authProviderConfigurationService = authProviderConfigurationService;
        _getAuthProviderConfigHandler = getAuthProviderConfigHandler;
        _updateAuthConfigDuringSetupHandler = updateAuthConfigDuringSetupHandler;
        _updateAuthConfigHandler = updateAuthConfigHandler;
    }

    [InstanceManagement]
    [HttpGet("auth-provider", Name = RouteNames.GetInstanceAuthProviderConfiguration)]
    [EndpointSummary("Get Auth Provider Configuration")]
    [EndpointDescription("Returns current auth provider configuration. Secrets are redacted.")]
    [ProducesResponseType(typeof(AuthProviderConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthProviderConfigurationDto>> GetAuthProviderConfiguration(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var configuration = await _getAuthProviderConfigHandler.QueryAsync(new GetAuthProviderConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [InstanceManagement]
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
            response = await _updateAuthConfigDuringSetupHandler.ExecuteAsync(
                new UpdateAuthProviderConfigurationDuringSetupCommand { Patch = configuration },
                cancellationToken);
        }
        else
        {
            var userId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
            if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

            response = await _updateAuthConfigHandler.ExecuteAsync(
                new UpdateAuthProviderConfigurationCommand { UserId = userId.Value, Patch = configuration },
                cancellationToken);
        }

        return HandleCommandResponse(response);
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
