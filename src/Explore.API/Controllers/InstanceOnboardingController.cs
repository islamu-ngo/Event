// ABOUTME: API controller for first-run instance onboarding wizard (one-time setup flow).
// ABOUTME: Provides status check, onboarding completion, secret validation, and setup-time auth provider config.

using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
public class InstanceOnboardingController : EventControllerBase
{
    private const string PreflightBlockedMessage = "Instance cannot be launched because critical launch requirements are not met. Please review the blocking issues and try again.";

    private static readonly ApiValidationProblemDescriptor CompleteValidationProblem = new(
        "instanceOnboarding",
        "Instance onboarding validation failed",
        "Instance onboarding completion failed.");

    private static readonly ApiValidationProblemDescriptor ProfileValidationProblem = new(
        "instanceOnboardingProfile",
        "Instance onboarding profile validation failed",
        "Instance onboarding profile save failed.");

    private static readonly ApiValidationProblemDescriptor AuthorizationPolicySyncValidationProblem = new(
        "instanceAuthorizationPolicyPackage",
        "Instance authorization policy package validation failed",
        "Instance authorization policy package sync failed.");

    private static readonly ApiValidationProblemDescriptor AuthorizationProviderVerifyValidationProblem = new(
        "instanceAuthorizationProviderVerification",
        "Instance authorization-provider verification failed",
        "Instance authorization-provider endpoint verification failed.");

    private readonly IMediator _mediator;
    private readonly ISetupSecretProvider _setupSecretProvider;
    private readonly IInstanceBootstrapAuditLogger _bootstrapAuditLogger;
    private readonly IAuthProviderConfigurationService _authProviderConfigurationService;
    private readonly ILogger<InstanceOnboardingController> _logger;
    private readonly IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto> _statusAssembler;

    public InstanceOnboardingController(
        IMediator mediator,
        ISetupSecretProvider setupSecretProvider,
        IInstanceBootstrapAuditLogger bootstrapAuditLogger,
        IAuthProviderConfigurationService authProviderConfigurationService,
        ILogger<InstanceOnboardingController> logger,
        IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto> statusAssembler)
    {
        _mediator = mediator;
        _setupSecretProvider = setupSecretProvider;
        _bootstrapAuditLogger = bootstrapAuditLogger;
        _authProviderConfigurationService = authProviderConfigurationService;
        _logger = logger;
        _statusAssembler = statusAssembler;
    }

    [AllowAnonymous]
    [PrivateNoStore]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("status", Name = RouteNames.GetInstanceOnboardingStatus)]
    [EndpointSummary("Get Instance Onboarding Status")]
    [EndpointDescription("Returns whether first-run onboarding is completed and whether the current user is instance admin.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceOnboardingStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalResource<InstanceOnboardingStatusDto>>> GetStatus(CancellationToken cancellationToken = default)
    {
        var status = await _mediator.Send(new GetInstanceOnboardingStatusQuery { SetupPrincipal = User }, cancellationToken);
        var resource = await _statusAssembler.ToResource(status, HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [SetupSecretRequired(requireIncomplete: true)]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPatch("profile", Name = RouteNames.SaveInstanceOnboardingProfile)]
    [EndpointSummary("Save Instance Onboarding Profile")]
    [EndpointDescription("Saves the non-secret instance profile while first-run setup authority is active.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SaveProfile(
        [FromBody] SelfHostOnboardingProfileDto profile,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new SaveInstanceOnboardingProfileCommand
        {
            Profile = profile
        }, cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : this.ToCommandValidationProblem(response, ProfileValidationProblem);
    }

    [Authorize]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPost("complete", Name = RouteNames.CompleteInstanceOnboarding)]
    [EndpointSummary("Complete Instance Onboarding")]
    [EndpointDescription("Completes first-run onboarding, assigns the current user as instance admin, and persists deployment mode.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Complete([FromBody] CompleteInstanceOnboardingRequest settings, CancellationToken cancellationToken = default)
    {
        var providerSubject = User.GetProviderSubject();
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue && !string.IsNullOrWhiteSpace(providerSubject))
        {
            currentUserId = Guid.CreateVersion7();
        }

        if (!currentUserId.HasValue)
        {
            _logger.LogWarning(
                "Instance onboarding complete rejected | Reason={Reason} Route={Route} Authenticated={IsAuthenticated} PlatformIdentityPresent={PlatformIdentityPresent} ProviderIdentityPresent={ProviderIdentityPresent}",
                "current_user_unresolved",
                RouteNames.CompleteInstanceOnboarding,
                User.Identity?.IsAuthenticated ?? false,
                User.GetPlatformUserId().HasValue,
                User.GetProviderIdentity() is not null);
            return this.ToAuthenticationRequiredProblem(detail: "Session expired. Please sign in again.");
        }

        var preflight = await _mediator.Send(new GetOnboardingPreflightQuery(), cancellationToken);
        if (!preflight.IsReadyToLaunch)
        {
            return this.ToValidationProblem(CompleteValidationProblem, PreflightBlockedMessage);
        }

        providerSubject ??= currentUserId.Value.ToString("D");
        var authProvider = User.GetAuthProvider();
        var email = User.GetEmail();
        var command = new CompleteInstanceOnboardingCommand
        {
            UserId = currentUserId.Value,
            Settings = settings,
            Email = email,
            FirstName = User.GetFirstName(),
            LastName = User.GetLastName(),
            Username = User.GetUsername(),
            AuthProvider = authProvider,
            AuthProviderId = User.GetProviderId(providerSubject, authProvider),
            EmailVerified = User.GetEmailVerified()
        };

        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CompleteValidationProblem);
        }

        _logger.LogWarning(
            "Instance onboarding completed | Route={Route} Outcome={Outcome}",
            RouteNames.CompleteInstanceOnboarding,
            "bootstrap_disabled");

        return Ok(response);
    }

    [Authorize(AuthenticationSchemes = Explore.Application.Constants.ApiAuthenticationSchemeNames.SetupSecret)]
    [SetupSecretRequired(requireIncomplete: true)]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPost("complete-local", Name = RouteNames.CompleteLocalInstanceOnboarding)]
    [EndpointSummary("Complete Local Instance Onboarding")]
    [EndpointDescription("Enrolls the initial Local administrator under setup authority. Private credential replacement is required before ordinary sign-in.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CompleteLocal(
        [FromBody] CompleteLocalInstanceOnboardingRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CompleteLocalInstanceOnboardingCommand(request, User), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToCommandValidationProblem(response, CompleteValidationProblem);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpPost("validate-secret", Name = RouteNames.ValidateInstanceSetupSecret)]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointSummary("Validate Setup Secret")]
    [EndpointDescription("Validates the provided setup secret. Returns whether the secret is correct. Rate limited to 5 attempts per minute.")]
    [ProducesResponseType(typeof(SetupSecretValidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SetupSecretValidationResultDto>> ValidateSecret(
        [FromBody] ValidateSetupSecretRequest request,
        CancellationToken cancellationToken = default)
    {
        SetupSecretValidationOutcome validation =
            await _setupSecretProvider.ValidateSecretAsync(request.Secret, cancellationToken);
        if (validation == SetupSecretValidationOutcome.SetupCompleted)
        {
            LogSetupSecretValidationAudit(
                InstanceBootstrapAuditEventType.SetupModeInactive,
                "inactive",
                ApiProblemCodes.SetupAlreadyCompleted);
            return this.ToGoneProblem(
                "Setup already completed",
                "Setup mode is no longer active for this instance.",
                ApiProblemCodes.SetupAlreadyCompleted);
        }

        var isValid = validation == SetupSecretValidationOutcome.Accepted;
        LogSetupSecretValidationAudit(
            isValid
                ? InstanceBootstrapAuditEventType.SetupSecretAccepted
                : InstanceBootstrapAuditEventType.SetupSecretRejected,
            isValid ? "accepted" : "rejected",
            isValid ? null : "invalid_setup_secret");

        return Ok(new SetupSecretValidationResultDto(isValid));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("auth-provider-configuration", Name = RouteNames.GetInstanceOnboardingAuthProviderConfiguration)]
    [EndpointSummary("Get Auth Provider Configuration (Public)")]
    [EndpointDescription("Returns auth provider configuration without secrets. Used by BFF at startup to discover configured providers.")]
    [ProducesResponseType(typeof(AuthProviderConfigurationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthProviderConfigurationDto>> GetAuthProviderConfiguration(CancellationToken cancellationToken = default)
    {
        var configuration = await _authProviderConfigurationService.ReadConfigurationAsync();
        return Ok(configuration);
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpGet("auth-provider-configuration/internal", Name = RouteNames.GetInstanceOnboardingAuthProviderConfigurationInternal)]
    [EndpointSummary("Get Auth Provider Configuration (Internal)")]
    [EndpointDescription("Returns auth provider configuration including secrets. For BFF internal use only. Protected by setup secret.")]
    [ProducesResponseType(typeof(AuthProviderConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthProviderConfigurationDto>> GetAuthProviderConfigurationInternal(CancellationToken cancellationToken = default)
    {
        var configuration = await _authProviderConfigurationService.ReadConfigurationWithSecretsAsync();
        return Ok(configuration);
    }

    /// <summary>
    /// Bootstraps an external Keycloak realm and persists runtime auth-provider configuration during setup.
    /// </summary>
    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPost("auth-provider-configuration/keycloak-bootstrap", Name = RouteNames.BootstrapInstanceOnboardingKeycloakRealm)]
    [EndpointSummary("Bootstrap Keycloak Realm (Setup)")]
    [EndpointDescription("Bootstraps an external Keycloak realm/client configuration during instance setup. Protected by setup secret; one-time admin credentials are not stored.")]
    [Consumes("application/json")]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> BootstrapKeycloakRealm([FromBody] KeycloakBootstrapRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new BootstrapKeycloakRealmCommand { BootstrapRequest = request },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAuthProviderProblem(response);
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpGet("authz-provider-configuration/internal", Name = RouteNames.GetInstanceOnboardingAuthorizationProviderConfigurationInternal)]
    [EndpointSummary("Get Authorization Provider Configuration (Internal)")]
    [EndpointDescription("Returns authorization provider configuration for setup flow. Protected by setup secret.")]
    [ProducesResponseType(typeof(AuthorizationProviderConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthorizationProviderConfigurationDto>> GetAuthorizationProviderConfigurationInternal(CancellationToken cancellationToken = default)
    {
        var configuration = await _mediator.Send(new GetAuthorizationProviderConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPost("authz-provider-configuration/sync", Name = RouteNames.SyncInstanceOnboardingAuthorizationPolicyPackage)]
    [EndpointSummary("Sync Authorization Policy Package (Setup)")]
    [EndpointDescription("Publishes the authorization policy package during instance setup. Protected by setup secret.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SyncAuthorizationPolicyPackage(
        [FromBody] AuthorizationPolicyPackageSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new SyncAuthorizationPolicyPackageCommand { Request = request },
            cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, AuthorizationPolicySyncValidationProblem);
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpGet("authz-provider-configuration/package", Name = RouteNames.DownloadInstanceOnboardingAuthorizationPolicyPackage)]
    [EndpointSummary("Download Authorization Policy Package (Setup)")]
    [EndpointDescription("Downloads a ZIP archive containing the authorization policy package and manual cerbosctl instructions. Protected by setup secret.")]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadAuthorizationPolicyPackage(CancellationToken cancellationToken = default)
    {
        try
        {
            var archive = await _mediator.Send(new DownloadAuthorizationPolicyPackageQuery(), cancellationToken);
            return File(archive.Content.ToArray(), archive.ContentType, archive.FileName);
        }
        catch (PolicyPackageUnavailableException ex)
        {
            _logger.LogWarning(ex, "Authorization policy package download is unavailable for this API deployment.");
            return AuthorizationPolicyPackageUnavailableProblem();
        }
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpPost("authz-provider-configuration/verify", Name = RouteNames.VerifyInstanceOnboardingAuthorizationProviderEndpoint)]
    [EndpointSummary("Verify Cerbos Authorization Endpoint")]
    [EndpointDescription("Verifies a Cerbos gRPC endpoint by calling its gRPC health service. Protected by setup secret.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> VerifyAuthorizationProviderEndpoint(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyCerbosEndpointRequest? request,
        CancellationToken cancellationToken = default)
    {
        var command = new VerifyCerbosEndpointCommand
        {
            GrpcEndpoint = request?.GrpcEndpoint ?? string.Empty
        };

        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, AuthorizationProviderVerifyValidationProblem);
        }

        return Ok(response);
    }


    private ActionResult AuthorizationPolicyPackageUnavailableProblem() =>
        this.ToServiceUnavailableProblem(
            "Authorization policy package unavailable",
            "The bundled Cerbos policy package is not available to this API deployment. Mount or bundle the package directory and retry the download.",
            ApiProblemCodes.AuthorizationPolicyPackageUnavailable);

    private void LogSetupSecretValidationAudit(
        InstanceBootstrapAuditEventType eventType,
        string outcome,
        string? failureCode = null)
    {
        _bootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
            eventType,
            Operation: "setup_secret_validate",
            Outcome: outcome,
            RouteName: RouteNames.ValidateInstanceSetupSecret,
            TraceId: HttpContext.TraceIdentifier,
            FailureCode: failureCode));
    }
}

public class ValidateSetupSecretRequest
{
    public string? Secret { get; set; }
}

public class VerifyCerbosEndpointRequest
{
    public string? GrpcEndpoint { get; set; }
}

public class UpdateDeploymentModeRequest
{
    public string? DeploymentMode { get; set; }
}
