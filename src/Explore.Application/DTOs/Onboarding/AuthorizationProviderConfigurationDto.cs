using Explore.Application.DTOs.Secrets;

namespace Explore.Application.DTOs.Onboarding;

public sealed record AuthorizationProviderConfigurationDto
{
    /// <summary>
    /// The authorization provider: "cerbos" for external Cerbos PDP, "local" for built-in RBAC.
    /// </summary>
    public string Provider { get; init; } = "local";

    /// <summary>
    /// The Cerbos gRPC endpoint (e.g., "https://cerbosgrpc.example.org:443").
    /// Only required when Provider is "cerbos".
    /// </summary>
    public string CerbosGrpcEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The Cerbos Admin API endpoint used for policy package publishing.
    /// Only required when Cerbos Admin API publishing is enabled.
    /// </summary>
    public string CerbosAdminEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// Whether a deployment-provided Admin API username is available. The value is never returned.
    /// </summary>
    public bool CerbosAdminUsernameConfigured { get; init; }

    /// <summary>
    /// Whether a deployment-provided Admin API password is available. The value is never returned.
    /// </summary>
    public bool CerbosAdminPasswordConfigured { get; init; }

    /// <summary>
    /// Whether a Cerbos gRPC endpoint is available from deployment configuration.
    /// The endpoint can prefill application-managed setup without selecting Cerbos or locking the provider choice.
    /// </summary>
    public bool CerbosDetectedFromEnvironment { get; init; }

    /// <summary>
    /// Whether the Cerbos gRPC endpoint was verified reachable via health check.
    /// </summary>
    public bool CerbosEndpointVerified { get; set; }

    /// <summary>
    /// Whether an authorization provider choice has already been saved.
    /// </summary>
    public bool AuthorizationProviderConfigured { get; init; }

    /// <summary>
    /// Whether the provider choice is authoritative deployment configuration rather than an application setting.
    /// </summary>
    public bool AuthorizationProviderManagedByDeployment { get; init; }

    /// <summary>
    /// Server-owned deployment reconciliation state: not-applicable, pending, ready, or failed.
    /// </summary>
    public string AuthorizationProviderBootstrapStatus { get; init; } = "not-applicable";

    /// <summary>
    /// Whether the deployment reconciliation published the bundled Cerbos policies.
    /// </summary>
    public bool CerbosPoliciesSynchronized { get; init; }

    /// <summary>
    /// Operator-safe deployment reconciliation guidance. Never contains endpoints or credentials.
    /// </summary>
    public string? AuthorizationProviderBootstrapMessage { get; init; }

    /// <summary>
    /// Ownership metadata for the Cerbos PDP endpoint/bootstrap value.
    /// </summary>
    public SecretOwnershipDto CerbosEndpointOwnership { get; init; } = new();

    /// <summary>
    /// Ownership metadata for Cerbos Admin API credentials. Values are never returned.
    /// </summary>
    public SecretOwnershipDto CerbosAdminCredentialsOwnership { get; init; } = new();
}
