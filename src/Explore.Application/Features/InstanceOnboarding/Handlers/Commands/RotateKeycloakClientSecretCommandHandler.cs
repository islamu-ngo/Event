using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class RotateKeycloakClientSecretCommandHandler(
    IAdminContext adminContext,
    IAuthProviderConfigurationService configurationService,
    IKeycloakBootstrapService keycloakBootstrapService,
    IJwtAuthorityRefreshNotifier jwtAuthorityRefreshNotifier,
    ILogger<RotateKeycloakClientSecretCommandHandler> logger)
    : IRequestHandler<RotateKeycloakClientSecretCommand, KeycloakClientSecretRotationResultDto>
{
    public async Task<KeycloakClientSecretRotationResultDto> Handle(
        RotateKeycloakClientSecretCommand request,
        CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
        {
            return Blocked("Only instance administrators can rotate Keycloak client secrets.");
        }

        var configuration = await configurationService.ReadConfigurationAsync();
        var ownershipMode = NormalizeOwnershipMode(configuration.KeycloakClientSecretOwnership.Mode);
        request.Request.SecretOwnershipMode = ownershipMode;

        var validator = new KeycloakClientSecretRotationRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return new KeycloakClientSecretRotationResultDto
            {
                Status = "blocked",
                Message = "Invalid Keycloak client-secret rotation request.",
                ClientId = request.Request.ClientId ?? string.Empty,
                SecretOwnershipMode = request.Request.SecretOwnershipMode,
                Operations = validationResult.Errors
                    .Select(error => new KeycloakRealmSyncOperationDto
                    {
                        OperationId = "keycloak-client-secret-rotation-validation",
                        Category = "client-secret",
                        TargetType = "client",
                        Target = request.Request.ClientId ?? string.Empty,
                        Action = "validate",
                        Status = "blocked",
                        Summary = "Rotation request validation failed.",
                        Reason = error.ErrorMessage
                    })
                    .ToArray()
            };
        }

        if (ownershipMode == "deployment-managed")
        {
            var clientId = configuration.KeycloakClientId;
            logger.LogInformation(
                "Keycloak client-secret rotation requires operator action. ActorId: {ActorId}, ClientId: {ClientId}, OwnershipMode: {OwnershipMode}, Result: {Result}",
                request.UserId,
                clientId,
                ownershipMode,
                "operator-action-required");

            return new KeycloakClientSecretRotationResultDto
            {
                Status = "operator-action-required",
                Message = "Keycloak client secret is deployment-managed.",
                ClientId = clientId,
                SecretOwnershipMode = ownershipMode,
                RequiresRestart = true,
                OperatorInstructions = "Rotate the Keycloak client secret in the deployment secret provider, update the matching Keycloak client, then restart or refresh the deployment. The platform did not contact Keycloak or store a new secret."
            };
        }

        var result = await keycloakBootstrapService.RotateClientSecretAsync(
            configuration,
            request.Request,
            cancellationToken);

        if (result.Status.Equals("rotated", StringComparison.OrdinalIgnoreCase))
        {
            configuration.KeycloakClientSecret = request.Request.NewClientSecret ?? string.Empty;
            await configurationService.ApplyConfigurationAsync(configuration);
            await jwtAuthorityRefreshNotifier.ReloadAsync(cancellationToken);
            result.AuthSchemesReloaded = true;
        }

        logger.LogInformation(
            "Keycloak client-secret rotation completed. ActorId: {ActorId}, ClientId: {ClientId}, OwnershipMode: {OwnershipMode}, Result: {Result}, AuthSchemesReloaded: {AuthSchemesReloaded}",
            request.UserId,
            result.ClientId,
            result.SecretOwnershipMode,
            result.Status,
            result.AuthSchemesReloaded);

        return result;
    }

    private static string NormalizeOwnershipMode(string? mode)
    {
        return mode?.Trim().Equals("deployment-managed", StringComparison.OrdinalIgnoreCase) == true
            ? "deployment-managed"
            : "application-managed";
    }

    private static KeycloakClientSecretRotationResultDto Blocked(string message)
    {
        return new KeycloakClientSecretRotationResultDto
        {
            Status = "blocked",
            Message = message
        };
    }
}
