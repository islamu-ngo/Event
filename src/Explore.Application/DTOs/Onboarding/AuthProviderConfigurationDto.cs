using Explore.Application.DTOs.Secrets;

namespace Explore.Application.DTOs.Onboarding;

public sealed record AuthProviderConfigurationDto
{
    // Primary authentication provider (Local Identity, Keycloak, or AT Protocol)
    public int PrimaryProviderId { get; init; } =
        (int)global::Explore.Domain.Enums.AuthenticationProviderKind.Local;
    public string PrimaryProviderCode { get; init; } = "local";
    public string PrimaryProviderName { get; init; } = "Local Identity";
    public bool LockPrimaryProvider { get; init; }

    // Keycloak
    public string KeycloakAuthority { get; init; } = string.Empty;
    public string KeycloakClientId { get; init; } = string.Empty;
    public string KeycloakClientSecret { get; set; } = string.Empty;
    public bool KeycloakDetectedFromEnvironment { get; init; }
    public SecretOwnershipDto KeycloakClientSecretOwnership { get; init; } = new();

    // ATProto Login
    public bool AtprotoLoginEnabled { get; init; }
    public string AtprotoPublicUrl { get; init; } = string.Empty;

    // Google SSO
    public bool GoogleSsoEnabled { get; init; }
    public string GoogleClientId { get; init; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;

    // Lock flags (for multi-tenant override control)
    public bool LockAtprotoLoginEnabled { get; init; }
    public bool LockGoogleSsoEnabled { get; init; }
}
