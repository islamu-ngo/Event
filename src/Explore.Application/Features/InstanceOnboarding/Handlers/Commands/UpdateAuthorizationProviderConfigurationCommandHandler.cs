using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateAuthorizationProviderConfigurationCommandHandler :
    ICommandHandler<UpdateAuthorizationProviderConfigurationCommand, BaseCommandResponse<Guid>>,
    ICommandHandler<UpdateAuthorizationProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateAuthorizationProviderConfigurationCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update authorization provider configuration.");
        }

        return await ApplyConfigurationAsync(request.Patch,
            token => _configurationService.ObserveConfigurationAsync(request, token), cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateAuthorizationProviderConfigurationDuringSetupCommand request,
        CancellationToken cancellationToken)
    {
        if (!await _setupSecretProvider.IsSetupModeActiveAsync(cancellationToken))
        {
            const string message = "Setup mode is no longer active.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        return await ApplyConfigurationAsync(request.Patch,
            token => _configurationService.ObserveConfigurationAsync(request, token), cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> ApplyConfigurationAsync(
        PatchAuthorizationProviderConfigurationDto configurationPatch,
        Func<CancellationToken, Task<ProviderConfigurationMutation>> observeOriginal,
        CancellationToken cancellationToken)
    {
        if (!configurationPatch.HasChanges() || configurationPatch.Configuration.Value is null)
        {
            const string message = "Authorization provider patch must include a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        await using var observation = await observeOriginal(cancellationToken);
        if (observation.AuthorizationCurrent.AuthorizationProviderManagedByDeployment)
            return BaseCommandResponse.Validation<Guid>(
                ["Change AUTHORIZATION_PROVIDER and the related server-side settings, then restart the deployment."],
                "Authorization provider configuration is managed by the deployment.");
        var validationResult = await observation.ValidateAsync(cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(x => x.ErrorMessage),
                "Invalid authorization provider configuration.");
        }

        await _configurationService.ApplyConfigurationAsync(observation, cancellationToken);

        return BaseCommandResponse.Success(
            Guid.Empty,
            "Authorization provider configuration updated successfully.");
    }
}
