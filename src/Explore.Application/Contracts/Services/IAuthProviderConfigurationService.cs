using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying authentication provider configuration settings.
/// </summary>
public interface IAuthProviderConfigurationService
{
    /// <summary>
    /// Reads current auth provider configuration from SystemSetting records.
    /// Runtime credential readiness is returned without credential material.
    /// </summary>
    Task<AuthProviderConfigurationDto> ReadConfigurationAsync();

    /// <summary>
    /// Applies auth provider configuration settings to SystemSetting records.
    /// </summary>
    /// <param name="configuration">The auth provider configuration to apply.</param>
    Task ApplyConfigurationAsync(AuthProviderConfigurationDto configuration,
        IReadOnlySet<string>? suppliedKeys = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any auth provider has been configured (at least one enabled).
    /// </summary>
    Task<bool> IsConfiguredAsync();

}
