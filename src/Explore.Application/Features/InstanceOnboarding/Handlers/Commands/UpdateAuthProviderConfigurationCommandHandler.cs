using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateAuthProviderConfigurationCommandHandler :
    ICommandHandler<UpdateAuthProviderConfigurationCommand, BaseCommandResponse<Guid>>,
    ICommandHandler<UpdateAuthProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IAuthProviderConfigurationService _configurationService;
    private readonly IAuthenticationProviderModeCacheInvalidator
        _authenticationProviderModeCacheInvalidator;
    private readonly IJwtAuthorityRefreshNotifier _jwtAuthorityRefreshNotifier;
    private readonly ISetupSecretProvider _setupSecretProvider;

    public UpdateAuthProviderConfigurationCommandHandler(
        IAdminContext adminContext,
        IAuthProviderConfigurationService configurationService,
        IAuthenticationProviderModeCacheInvalidator
            authenticationProviderModeCacheInvalidator,
        IJwtAuthorityRefreshNotifier jwtAuthorityRefreshNotifier,
        ISetupSecretProvider setupSecretProvider)
    {
        _adminContext = adminContext;
        _configurationService = configurationService;
        _authenticationProviderModeCacheInvalidator =
            authenticationProviderModeCacheInvalidator;
        _jwtAuthorityRefreshNotifier = jwtAuthorityRefreshNotifier;
        _setupSecretProvider = setupSecretProvider;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateAuthProviderConfigurationCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update auth provider configuration.");
        }

        return await ApplyConfigurationAsync(request.Patch,
            token => _configurationService.ObserveConfigurationAsync(request, token), cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateAuthProviderConfigurationDuringSetupCommand request,
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
        PatchAuthProviderConfigurationDto configurationPatch,
        Func<CancellationToken, Task<ProviderConfigurationMutation>> observeOriginal,
        CancellationToken cancellationToken)
    {
        if (!configurationPatch.HasChanges() || configurationPatch.Configuration.Value is null)
        {
            const string message = "Authentication provider patch must include a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        await using var observation = await observeOriginal(cancellationToken);
        var validationResult = await observation.ValidateAsync(cancellationToken);
        if (!validationResult.IsValid)
        {
            if (validationResult.Errors is [{ PropertyName: "User" or "Provider" } failure])
                return BaseCommandResponse.Validation<Guid>([failure.ErrorMessage], failure.ErrorMessage);
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(x => x.ErrorMessage),
                "Invalid auth provider configuration.");
        }

        await _configurationService.ApplyConfigurationAsync(observation, cancellationToken);
        _authenticationProviderModeCacheInvalidator.InvalidateInstanceMode();
        await _jwtAuthorityRefreshNotifier.ReloadAsync(cancellationToken);

        return BaseCommandResponse.Success(
            Guid.Empty,
            "Authentication provider configuration updated successfully.");
    }
}
