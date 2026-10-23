using Explore.Application.Authorization;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying authorization provider configuration settings.
/// </summary>
public interface IAuthorizationProviderConfigurationService
{
    Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command, CancellationToken token);
    Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command, CancellationToken token);
    Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token);

    /// <summary>
    /// Reads current authorization provider configuration from SystemSetting records
    /// and environment-detected state.
    /// </summary>
    Task<AuthorizationProviderConfigurationDto> ReadConfigurationAsync();

    /// <summary>
    /// Applies authorization provider configuration to SystemSetting records.
    /// </summary>
    Task ApplyConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command,
        CancellationToken cancellationToken = default);

    Task ApplySetupConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an authorization provider has been explicitly configured.
    /// </summary>
    Task<bool> IsConfiguredAsync();

    /// <summary>
    /// Verifies that a Cerbos gRPC endpoint is reachable by performing a health check.
    /// Returns true if the endpoint responds, false otherwise.
    /// </summary>
    Task<bool> VerifyCerbosEndpointAsync(string grpcEndpoint, CancellationToken cancellationToken = default);

    Task<bool> VerifyCerbosAdminEndpointAsync(string adminEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles an explicit deployment-selected provider. Local becomes ready immediately;
    /// Cerbos requires PDP health and an authenticated publication declaration; no Admin API call occurs.
    /// </summary>
    Task<AuthorizationProviderReconciliationResult> ReconcileDeploymentProviderAsync(
        CancellationToken cancellationToken = default);
}
