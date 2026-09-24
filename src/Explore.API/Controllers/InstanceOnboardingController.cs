using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
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

    private readonly IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto> _onboardingStatusQuery;
    private readonly IQueryHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto> _onboardingPreflightQuery;
    private readonly IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto> _authzProviderConfigQuery;
    private readonly IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive> _downloadPolicyPackageQuery;
    private readonly ICommandHandler<SaveInstanceOnboardingProfileCommand, BaseCommandResponse<Guid>> _saveProfileCommand;
    private readonly ICommandHandler<CompleteInstanceOnboardingCommand, BaseCommandResponse<Guid>> _completeOnboardingCommand;
    private readonly ICommandHandler<CompleteLocalInstanceOnboardingCommand, BaseCommandResponse<Guid>> _completeLocalOnboardingCommand;
    private readonly ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>> _syncPolicyPackageCommand;
    private readonly ICommandHandler<VerifyCerbosEndpointCommand, BaseCommandResponse<Guid>> _verifyCerbosEndpointCommand;
    private readonly IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> _identityQuery;
    private readonly IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities> _lifecycleCapabilities;
    private readonly ISetupSecretProvider _setupSecretProvider;
    private readonly IInstanceBootstrapAuditLogger _bootstrapAuditLogger;
    private readonly IAuthProviderConfigurationService _authProviderConfigurationService;
    private readonly IVisitorAccessCapabilityResolver _visitorAccessCapabilityResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InstanceOnboardingController> _logger;
    private readonly IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto> _statusAssembler;
    private readonly IQueryHandler<GetInstanceOnboardingJourneyQuery, InstanceOnboardingJourneyDto> _journeyQuery;
    private readonly IResourceAssembler<InstanceOnboardingJourneyDto, InstanceOnboardingJourneyDto> _journeyAssembler;

    public InstanceOnboardingController(
        IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto> onboardingStatusQuery,
        IQueryHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto> onboardingPreflightQuery,
        IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto> authzProviderConfigQuery,
        IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive> downloadPolicyPackageQuery,
        ICommandHandler<SaveInstanceOnboardingProfileCommand, BaseCommandResponse<Guid>> saveProfileCommand,
        ICommandHandler<CompleteInstanceOnboardingCommand, BaseCommandResponse<Guid>> completeOnboardingCommand,
        ICommandHandler<CompleteLocalInstanceOnboardingCommand, BaseCommandResponse<Guid>> completeLocalOnboardingCommand,
        ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>> syncPolicyPackageCommand,
        ICommandHandler<VerifyCerbosEndpointCommand, BaseCommandResponse<Guid>> verifyCerbosEndpointCommand,
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities> lifecycleCapabilities,
        ISetupSecretProvider setupSecretProvider,
        IInstanceBootstrapAuditLogger bootstrapAuditLogger,
        IAuthProviderConfigurationService authProviderConfigurationService,
        ILogger<InstanceOnboardingController> logger,
        IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto> statusAssembler,
        IVisitorAccessCapabilityResolver visitorAccessCapabilityResolver,
        ITenantContext tenantContext,
        IQueryHandler<GetInstanceOnboardingJourneyQuery, InstanceOnboardingJourneyDto> journeyQuery,
        IResourceAssembler<InstanceOnboardingJourneyDto, InstanceOnboardingJourneyDto> journeyAssembler)
    {
        _onboardingStatusQuery = onboardingStatusQuery;
        _onboardingPreflightQuery = onboardingPreflightQuery;
        _authzProviderConfigQuery = authzProviderConfigQuery;
        _downloadPolicyPackageQuery = downloadPolicyPackageQuery;
        _saveProfileCommand = saveProfileCommand;
        _completeOnboardingCommand = completeOnboardingCommand;
        _completeLocalOnboardingCommand = completeLocalOnboardingCommand;
        _syncPolicyPackageCommand = syncPolicyPackageCommand;
        _verifyCerbosEndpointCommand = verifyCerbosEndpointCommand;
        _identityQuery = identityQuery;
        _lifecycleCapabilities = lifecycleCapabilities;
        _setupSecretProvider = setupSecretProvider;
        _bootstrapAuditLogger = bootstrapAuditLogger;
        _authProviderConfigurationService = authProviderConfigurationService;
        _logger = logger;
        _statusAssembler = statusAssembler;
        _visitorAccessCapabilityResolver = visitorAccessCapabilityResolver;
        _tenantContext = tenantContext;
        _journeyQuery = journeyQuery;
        _journeyAssembler = journeyAssembler;
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
        var status = await _onboardingStatusQuery.QueryAsync(new GetInstanceOnboardingStatusQuery { SetupPrincipal = User }, cancellationToken);
        var resource = await _statusAssembler.ToResource(status, HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [PrivateNoStore]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpGet("journey", Name = RouteNames.GetInstanceOnboardingJourney)]
    [EndpointSummary("Get Instance Onboarding Journey")]
    [EndpointDescription("Returns one fail-closed setup snapshot with provider readiness, persisted profile, generation, checks and authorized HAL actions.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceOnboardingJourneyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceOnboardingJourneyDto>>> GetJourney(CancellationToken cancellationToken = default)
    {
        var setup = await _setupSecretProvider.ValidateSecretAsync(Request.Headers["X-Setup-Secret"].FirstOrDefault(), cancellationToken);
        if (setup != SetupSecretValidationOutcome.Accepted)
        {
            var status = await _onboardingStatusQuery.QueryAsync(new() { SetupPrincipal = User }, cancellationToken);
            if (!status.IsCurrentUserInstanceAdmin)
                return this.ToForbiddenProblem(detail: "Active setup or instance administrator authority is required.");
        }
        var journey = await _journeyQuery.QueryAsync(new() { SetupPrincipal = User }, cancellationToken);
        return Ok(await _journeyAssembler.ToResource(journey, HttpContext));
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
        var response = await _saveProfileCommand.ExecuteAsync(new SaveInstanceOnboardingProfileCommand
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
        var currentUserId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
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

        var preflight = await _onboardingPreflightQuery.QueryAsync(new GetOnboardingPreflightQuery(), cancellationToken);
        if (!preflight.IsReadyToLaunch)
        {
            return this.ToValidationProblem(CompleteValidationProblem, PreflightBlockedMessage);
        }

        if (await CheckCompletionJourneyAsync(settings.ExpectedJourneyGeneration, cancellationToken) is { } conflict)
            return conflict;

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

        var response = await _completeOnboardingCommand.ExecuteAsync(command, cancellationToken);
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
        if (await CheckCompletionJourneyAsync(request.Settings?.ExpectedJourneyGeneration, cancellationToken) is { } conflict)
            return conflict;

        var response = await _completeLocalOnboardingCommand.ExecuteAsync(new CompleteLocalInstanceOnboardingCommand(request, User), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToCommandValidationProblem(response, CompleteValidationProblem);
    }

    private async Task<ObjectResult?> CheckCompletionJourneyAsync(string? expectedGeneration, CancellationToken cancellationToken)
    {
        var journey = await _journeyQuery.QueryAsync(new() { SetupPrincipal = User }, cancellationToken);
        if (journey.State == "Available" && journey.Preflight is { IsReadyToLaunch: true }
            && !string.IsNullOrWhiteSpace(expectedGeneration)
            && string.Equals(expectedGeneration, journey.Generation, StringComparison.Ordinal))
            return null;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Setup state changed",
            Detail = "Refresh authoritative setup status before submitting a new completion request."
        };
        problem.Extensions["_links"] = new Dictionary<string, object>
        {
            ["refresh"] = new { href = "/api/instanceonboarding/journey", method = "GET" }
        };
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
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
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<AuthProviderConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AuthProviderConfigurationDto>>> GetAuthProviderConfiguration(CancellationToken cancellationToken = default)
    {
        var configuration = await _authProviderConfigurationService.ReadConfigurationAsync();
        configuration = configuration with
        {
            VisitorAccess = VisitorAccessCapabilityDto.From(
                await _visitorAccessCapabilityResolver.ResolveAsync(_tenantContext.TenantId, cancellationToken))
        };
        var capabilities = await _lifecycleCapabilities.QueryAsync(new GetLocalIdentityLifecycleCapabilitiesQuery(PublicDiscovery: true), cancellationToken);
        var links = LocalIdentityLifecycleLinkPolicy.GetLinks(capabilities).ToDictionary(
            definition => definition.Rel,
            definition => new HalLink
            {
                Href = Url.RouteUrl(definition.RouteName, definition.RouteValues)
                    ?? throw new InvalidOperationException("The Local lifecycle route is not registered."),
                Method = definition.Method,
                Title = definition.Title
            });
        links.Add(LinkRelations.Self, HalLink.Create(Url.RouteUrl(RouteNames.GetInstanceOnboardingAuthProviderConfiguration)
            ?? throw new InvalidOperationException("The authentication discovery route is not registered.")));
        foreach (var destination in configuration.VisitorAccess.SignupDestinations)
        {
            links.Add(LinkRelations.VisitorSignupPrefix + destination.Provider.ToString().ToLowerInvariant(),
                HalLink.Create(destination.Url));
        }
        return Ok(new HalResource<AuthProviderConfigurationDto>(configuration, links));
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [PrivateNoStore]
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
        var configuration = await _authzProviderConfigQuery.QueryAsync(new GetAuthorizationProviderConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [AllowAnonymous]
    [SetupSecretRequired]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [EndpointClassification(EndpointClass.Admin)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
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
        var response = await _syncPolicyPackageCommand.ExecuteAsync(
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
            var archive = await _downloadPolicyPackageQuery.QueryAsync(new DownloadAuthorizationPolicyPackageQuery(), cancellationToken);
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

        var response = await _verifyCerbosEndpointCommand.ExecuteAsync(command, cancellationToken);
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
