using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateAuthProviderConfigurationCommandHandler :
    IRequestHandler<UpdateAuthProviderConfigurationCommand, BaseCommandResponse<Guid>>,
    IRequestHandler<UpdateAuthProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IUserRepository _userRepository;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IAuthProviderConfigurationService _configurationService;
    private readonly IAuthenticationProviderModeCacheInvalidator
        _authenticationProviderModeCacheInvalidator;
    private readonly IJwtAuthorityRefreshNotifier _jwtAuthorityRefreshNotifier;
    private readonly ISetupSecretProvider _setupSecretProvider;

    public UpdateAuthProviderConfigurationCommandHandler(
        IAdminContext adminContext,
        IUserRepository userRepository,
        IUserExternalLoginRepository userExternalLoginRepository,
        IAuthProviderConfigurationService configurationService,
        IAuthenticationProviderModeCacheInvalidator
            authenticationProviderModeCacheInvalidator,
        IJwtAuthorityRefreshNotifier jwtAuthorityRefreshNotifier,
        ISetupSecretProvider setupSecretProvider)
    {
        _adminContext = adminContext;
        _userRepository = userRepository;
        _userExternalLoginRepository = userExternalLoginRepository;
        _configurationService = configurationService;
        _authenticationProviderModeCacheInvalidator =
            authenticationProviderModeCacheInvalidator;
        _jwtAuthorityRefreshNotifier = jwtAuthorityRefreshNotifier;
        _setupSecretProvider = setupSecretProvider;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateAuthProviderConfigurationCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update auth provider configuration.");
        }

        return await ApplyConfigurationAsync(request.Patch, request.UserId, cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateAuthProviderConfigurationDuringSetupCommand request,
        CancellationToken cancellationToken)
    {
        if (!await _setupSecretProvider.IsSetupModeActiveAsync(cancellationToken))
        {
            const string message = "Setup mode is no longer active.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        return await ApplyConfigurationAsync(request.Patch, currentAdminUserId: null, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> ApplyConfigurationAsync(
        PatchAuthProviderConfigurationDto configurationPatch,
        Guid? currentAdminUserId,
        CancellationToken cancellationToken)
    {
        var currentConfiguration = await _configurationService.ReadConfigurationAsync();
        if (!configurationPatch.HasChanges() || configurationPatch.Configuration.Value is null)
        {
            const string message = "Authentication provider patch must include a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var patch = configurationPatch.Configuration.Value;
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = patch.PrimaryProviderId,
            PrimaryProviderCode = string.Empty,
            PrimaryProviderName = string.Empty,
            LockPrimaryProvider = patch.LockPrimaryProvider,
            KeycloakAuthority = patch.KeycloakAuthority,
            KeycloakClientId = patch.KeycloakClientId,
            KeycloakClientSecret = patch.KeycloakClientSecret,
            KeycloakDetectedFromEnvironment = currentConfiguration.KeycloakDetectedFromEnvironment,
            KeycloakClientSecretOwnership = currentConfiguration.KeycloakClientSecretOwnership,
            AtprotoLoginEnabled = patch.AtprotoLoginEnabled,
            AtprotoPublicUrl = patch.AtprotoPublicUrl,
            GoogleSsoEnabled = patch.GoogleSsoEnabled,
            GoogleClientId = patch.GoogleClientId,
            GoogleClientSecret = patch.GoogleClientSecret,
            LockAtprotoLoginEnabled = patch.LockAtprotoLoginEnabled,
            LockGoogleSsoEnabled = patch.LockGoogleSsoEnabled,
            KeycloakPublicOnboardingPolicy = patch.KeycloakPublicOnboardingPolicy,
            KeycloakPublicSignupUrl = patch.KeycloakPublicSignupUrl,
            GooglePublicOnboardingPolicy = patch.GooglePublicOnboardingPolicy,
            GooglePublicSignupUrl = patch.GooglePublicSignupUrl
        };
        var validator = new AuthProviderConfigurationDtoValidator(currentConfiguration);
        var validationResult = await validator.ValidateAsync(configuration, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(x => x.ErrorMessage),
                "Invalid auth provider configuration.");
        }

        if (currentAdminUserId.HasValue)
        {
            var currentUser = await _userRepository.GetById(currentAdminUserId.Value);
            if (currentUser == null)
            {
                const string message = "Current user could not be resolved.";
                return BaseCommandResponse.Validation<Guid>([message], message);
            }

            var currentUserLogins = await _userExternalLoginRepository.GetByUser(currentAdminUserId.Value);
            if (!AuthenticationProviderLockoutPolicy
                    .PreservesCurrentAdministratorAccess(
                        currentUserLogins,
                        configuration))
            {
                const string message = "Cannot disable all authentication providers linked to your current admin account.";
                return BaseCommandResponse.Validation<Guid>([message], message);
            }
        }

        await _configurationService.ApplyConfigurationAsync(configuration, cancellationToken: cancellationToken);
        _authenticationProviderModeCacheInvalidator.InvalidateInstanceMode();
        await _jwtAuthorityRefreshNotifier.ReloadAsync(cancellationToken);

        return BaseCommandResponse.Success(
            Guid.Empty,
            "Authentication provider configuration updated successfully.");
    }
}
