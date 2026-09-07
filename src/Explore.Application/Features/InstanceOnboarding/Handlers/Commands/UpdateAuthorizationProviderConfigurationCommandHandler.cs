using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Utilities;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateAuthorizationProviderConfigurationCommandHandler :
    IRequestHandler<UpdateAuthorizationProviderConfigurationCommand, BaseCommandResponse<Guid>>,
    IRequestHandler<UpdateAuthorizationProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IAuthorizationProviderConfigurationService _configurationService;
    private readonly ISetupSecretProvider _setupSecretProvider;

    public UpdateAuthorizationProviderConfigurationCommandHandler(
        IAdminContext adminContext,
        IAuthorizationProviderConfigurationService configurationService,
        ISetupSecretProvider setupSecretProvider)
    {
        _adminContext = adminContext;
        _configurationService = configurationService;
        _setupSecretProvider = setupSecretProvider;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateAuthorizationProviderConfigurationCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update authorization provider configuration.");
        }

        return await ApplyConfigurationAsync(request.Patch, cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateAuthorizationProviderConfigurationDuringSetupCommand request,
        CancellationToken cancellationToken)
    {
        if (!await _setupSecretProvider.IsSetupModeActiveAsync(cancellationToken))
        {
            const string message = "Setup mode is no longer active.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        return await ApplyConfigurationAsync(request.Patch, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> ApplyConfigurationAsync(
        PatchAuthorizationProviderConfigurationDto configurationPatch,
        CancellationToken cancellationToken)
    {
        var currentConfiguration = await _configurationService.ReadConfigurationAsync();
        if (currentConfiguration.AuthorizationProviderManagedByDeployment)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Change AUTHORIZATION_PROVIDER and the related server-side settings, then restart the deployment."],
                "Authorization provider configuration is managed by the deployment.");
        }

        if (!configurationPatch.HasChanges() || configurationPatch.Configuration.Value is null)
        {
            const string message = "Authorization provider patch must include a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var patch = configurationPatch.Configuration.Value;
        var configuration = new AuthorizationProviderConfigurationDto
        {
            Provider = patch.Provider,
            CerbosGrpcEndpoint = patch.CerbosGrpcEndpoint,
            CerbosAdminEndpoint = patch.CerbosAdminEndpoint,
            CerbosDetectedFromEnvironment = currentConfiguration.CerbosDetectedFromEnvironment,
            AuthorizationProviderConfigured = currentConfiguration.AuthorizationProviderConfigured,
            AuthorizationProviderManagedByDeployment = currentConfiguration.AuthorizationProviderManagedByDeployment,
            AuthorizationProviderBootstrapStatus = currentConfiguration.AuthorizationProviderBootstrapStatus,
            CerbosPoliciesSynchronized = currentConfiguration.CerbosPoliciesSynchronized,
            AuthorizationProviderBootstrapMessage = currentConfiguration.AuthorizationProviderBootstrapMessage,
            CerbosEndpointOwnership = currentConfiguration.CerbosEndpointOwnership,
            CerbosAdminCredentialsOwnership = currentConfiguration.CerbosAdminCredentialsOwnership
        };

        var validator = new AuthorizationProviderConfigurationDtoValidator();
        var validationResult = await validator.ValidateAsync(configuration, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(x => x.ErrorMessage),
                "Invalid authorization provider configuration.");
        }

        configuration.CerbosGrpcEndpoint = GrpcEndpointNormalizer.Normalize(configuration.CerbosGrpcEndpoint);

        if (configuration.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase))
        {
            var isReachable = await _configurationService.VerifyCerbosEndpointAsync(
                configuration.CerbosGrpcEndpoint,
                cancellationToken);

            if (!isReachable)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Ensure the endpoint is reachable and serving the gRPC health service."],
                    "Cerbos gRPC endpoint could not be verified.");
            }

            if (!string.IsNullOrWhiteSpace(configuration.CerbosAdminEndpoint))
            {
                var isAdminEndpointAllowed = await _configurationService.VerifyCerbosAdminEndpointAsync(
                    configuration.CerbosAdminEndpoint,
                    cancellationToken);

                if (!isAdminEndpointAllowed)
                {
                    return BaseCommandResponse.Validation<Guid>(
                        ["Use an HTTPS Admin API endpoint without credentials, query, fragment, or local/private network address components."],
                        "Cerbos Admin API endpoint is not allowed.");
                }
            }

            configuration.CerbosEndpointVerified = true;
        }

        await _configurationService.ApplyConfigurationAsync(configuration);

        return BaseCommandResponse.Success(
            Guid.Empty,
            "Authorization provider configuration updated successfully.");
    }
}
