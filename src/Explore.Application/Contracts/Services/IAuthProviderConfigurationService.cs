// ABOUTME: Service contract for managing instance-level authentication provider configuration.
// ABOUTME: Handles reading and applying auth provider settings (Keycloak, ATProto, Google SSO).

using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying authentication provider configuration settings.
/// </summary>
public interface IAuthProviderConfigurationService
{
    /// <summary>
    /// Reads current auth provider configuration from SystemSetting records.
    /// Secrets are not returned (write-only).
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

    /// <summary>
    /// Reads full auth provider configuration including secrets.
    /// For internal use by dynamic auth scheme registration only — never expose via API.
    /// </summary>
    Task<AuthProviderConfigurationDto> ReadConfigurationWithSecretsAsync();
}
