namespace Explore.Infrastructure.Services.Keycloak;

public static class KeycloakFailureCodes
{
    public const string AuthFailed = "keycloak_auth_failed";
    public const string Timeout = "keycloak_timeout";
    public const string Unreachable = "keycloak_unreachable";
    public const string InvalidResponse = "keycloak_invalid_response";
    public const string InvalidUrl = "keycloak_invalid_url";
    public const string UnsafeHost = "keycloak_unsafe_host";
    public const string InvalidAuthority = "keycloak_invalid_authority";
    public const string RealmCheckFailed = "keycloak_realm_check_failed";
    public const string RealmNotFound = "keycloak_realm_not_found";
    public const string RealmCreateFailed = "keycloak_realm_create_failed";
    public const string ClientLookupFailed = "keycloak_client_lookup_failed";
    public const string ClientNotFound = "keycloak_client_not_found";
    public const string ClientSecretUpdateFailed = "keycloak_client_secret_update_failed";
    public const string OfflineAccessRoleNotFound = "keycloak_offline_access_role_not_found";
    public const string DefaultRoleNotFound = "keycloak_default_role_not_found";
    public const string OfflineAccessRoleUpdateFailed = "keycloak_offline_access_role_update_failed";
    public const string ClientScopeNotFound = "keycloak_client_scope_not_found";
    public const string ClientScopeUpdateFailed = "keycloak_client_scope_update_failed";
    public const string OfflineAccessScopeMappingFailed = "keycloak_offline_access_scope_mapping_failed";
    public const string ClientCreateFailed = "keycloak_client_create_failed";
    public const string BootstrapValidationFailed = "keycloak_bootstrap_validation_failed";
}
