using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying authentication provider configuration settings.
/// </summary>
public interface IAuthProviderConfigurationService
{
    Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationCommand command, CancellationToken token);
    Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command, CancellationToken token);
    Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token);

    /// <summary>
    /// Reads current auth provider configuration from SystemSetting records.
    /// Runtime credential readiness is returned without credential material.
    /// </summary>
    Task<AuthProviderConfigurationDto> ReadConfigurationAsync();

    /// <summary>
    /// Applies auth provider configuration settings to SystemSetting records.
    /// </summary>
    Task ApplyConfigurationAsync(UpdateAuthProviderConfigurationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Explicit initial setup entry; requires the existing active setup authority.</summary>
    Task ApplySetupConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any auth provider has been configured (at least one enabled).
    /// </summary>
    Task<bool> IsConfiguredAsync();

}
