using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class BootstrapKeycloakRealmCommandHandler
    : IRequestHandler<BootstrapKeycloakRealmCommand, BaseCommandResponse<Guid>>
{
    private readonly IKeycloakBootstrapService _keycloakBootstrapService;
    private readonly IAuthProviderConfigurationService _configurationService;
    private readonly IJwtAuthorityRefreshNotifier _jwtAuthorityRefreshNotifier;
    private readonly IInstanceBootstrapAuditLogger _bootstrapAuditLogger;
    private readonly ILogger<BootstrapKeycloakRealmCommandHandler> _logger;

    public BootstrapKeycloakRealmCommandHandler(
        IKeycloakBootstrapService keycloakBootstrapService,
        IAuthProviderConfigurationService configurationService,
        IJwtAuthorityRefreshNotifier jwtAuthorityRefreshNotifier,
        IInstanceBootstrapAuditLogger bootstrapAuditLogger,
        ILogger<BootstrapKeycloakRealmCommandHandler> logger)
    {
        _keycloakBootstrapService = keycloakBootstrapService;
        _configurationService = configurationService;
        _jwtAuthorityRefreshNotifier = jwtAuthorityRefreshNotifier;
        _bootstrapAuditLogger = bootstrapAuditLogger;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        BootstrapKeycloakRealmCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new KeycloakBootstrapRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.BootstrapRequest, cancellationToken);
        if (!validationResult.IsValid)
        {
            LogKeycloakBootstrapAudit(
                InstanceBootstrapAuditEventType.KeycloakBootstrapFailed,
                request.BootstrapRequest,
                "validation_failed",
                "keycloak_bootstrap_validation_failed");

            return BaseCommandResponse.Failure<Guid>(
                "keycloak_bootstrap_validation_failed",
                "Invalid Keycloak bootstrap request.",
                validationResult.Errors.Select(x => x.ErrorMessage));
        }

        LogKeycloakBootstrapAudit(
            InstanceBootstrapAuditEventType.KeycloakBootstrapStarted,
            request.BootstrapRequest,
            "started");

        KeycloakBootstrapResultDto bootstrapResult = await _keycloakBootstrapService.BootstrapAsync(
            request.BootstrapRequest,
            cancellationToken);

        if (!bootstrapResult.Success)
        {
            LogKeycloakBootstrapAudit(
                InstanceBootstrapAuditEventType.KeycloakBootstrapFailed,
                request.BootstrapRequest,
                "failed",
                bootstrapResult.FailureCode ?? "keycloak_bootstrap_failed");

            _logger.LogWarning(
                "Keycloak bootstrap failed. Realm: {Realm}, BlazorClientId: {BlazorClientId}, ApiClientId: {ApiClientId}, Mode: {Mode}, FailureCode: {FailureCode}",
                request.BootstrapRequest.Realm,
                request.BootstrapRequest.BlazorClientId,
                request.BootstrapRequest.ApiClientId,
                request.BootstrapRequest.Mode,
                bootstrapResult.FailureCode);

            string message = string.IsNullOrWhiteSpace(bootstrapResult.Message)
                ? "Keycloak bootstrap failed."
                : bootstrapResult.Message;
            return BaseCommandResponse.Failure<Guid>(
                bootstrapResult.FailureCode ?? "keycloak_bootstrap_failed",
                message,
                [message]);
        }

        var runtimeConfiguration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Keycloak,
            PrimaryProviderCode = "keycloak",
            PrimaryProviderName = "Keycloak",
            KeycloakAuthority = BuildRealmAuthority(
                request.BootstrapRequest.KeycloakBaseUrl,
                request.BootstrapRequest.Realm),
            KeycloakClientId = request.BootstrapRequest.BlazorClientId,
            KeycloakClientSecret = request.BootstrapRequest.BlazorClientSecret
        };

        await _configurationService.ApplyConfigurationAsync(runtimeConfiguration);
        await _jwtAuthorityRefreshNotifier.ReloadAsync(cancellationToken);

        LogKeycloakBootstrapAudit(
            InstanceBootstrapAuditEventType.KeycloakBootstrapSucceeded,
            request.BootstrapRequest,
            "succeeded");

        _logger.LogInformation(
            "Keycloak bootstrap succeeded. Realm: {Realm}, BlazorClientId: {BlazorClientId}, ApiClientId: {ApiClientId}, Mode: {Mode}",
            request.BootstrapRequest.Realm,
            request.BootstrapRequest.BlazorClientId,
            request.BootstrapRequest.ApiClientId,
            request.BootstrapRequest.Mode);

        return BaseCommandResponse.Success(
            Guid.Empty,
            string.IsNullOrWhiteSpace(bootstrapResult.Message)
                ? "Keycloak bootstrap completed successfully."
                : bootstrapResult.Message);
    }

    private static string BuildRealmAuthority(string keycloakBaseUrl, string realm)
    {
        return $"{keycloakBaseUrl.TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}";
    }

    private void LogKeycloakBootstrapAudit(
        InstanceBootstrapAuditEventType eventType,
        KeycloakBootstrapRequestDto request,
        string outcome,
        string? failureCode = null)
    {
        _bootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
            eventType,
            Operation: "keycloak_bootstrap",
            Outcome: outcome,
            FailureCode: failureCode,
            Provider: "keycloak",
            Mode: request.Mode.ToString(),
            Realm: request.Realm,
            ClientId: request.BlazorClientId));
    }
}
