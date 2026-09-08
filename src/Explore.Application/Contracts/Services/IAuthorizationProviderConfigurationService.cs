using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying authorization provider configuration settings.
/// </summary>
public interface IAuthorizationProviderConfigurationService
{
    /// <summary>
    /// Reads current authorization provider configuration from SystemSetting records
    /// and environment-detected state.
    /// </summary>
    Task<AuthorizationProviderConfigurationDto> ReadConfigurationAsync();

    /// <summary>
    /// Applies authorization provider configuration to SystemSetting records.
    /// </summary>
    Task ApplyConfigurationAsync(AuthorizationProviderConfigurationDto configuration);

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
    /// Cerbos is ready only after PDP verification and policy publication both succeed.
    /// </summary>
    Task<AuthorizationProviderReconciliationResult> ReconcileDeploymentProviderAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null);
}
