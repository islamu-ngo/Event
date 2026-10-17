using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.Configuration;

/// <summary>
/// Admits coordinates before any provider call. Provider ACLs must independently enforce
/// these consumer boundaries; an admitted request is not evidence of a scoped identity.
/// Data Protection uses its separate ring/admission loader, never bulk configuration.
/// </summary>
public static class SecretAcquisitionPolicy
{
    private static readonly string[] ApiRoots =
    [
        "/runtime/api/authentication/keycloak", "/runtime/api/keycloak/lifecycle",
        "/runtime/api/authorization/cerbos",
        "/database", "/database/erasure", "/database/identity", "/postgresql",
        "/api", "/api/operator-identity", "/api/operatoridentity",
        "/api/bootstrap", "/api/instance-bootstrap", "/api/instancebootstrap",
        "/api/controlplane", "/api/control-plane", "/api/webpush", "/api/web-push",
        "/api/ratelimiting", "/api/rate-limiting",
        "/mcp", "/ai", "/storage", "/smtp", "/stripe", "/integrations/listmonk",
        "/setup", "/promotions", "/admissions", "/ticketing/recovery",
        "/atproto", "/webhook", "/messaging", "/reporting", "/registration-providers", "/analytics", "/localization"
    ];
    private static readonly string[] BffRoots =
        ["/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google", "/runtime/bff/atproto/oauth"];
    private static readonly string[] MigrationRoots =
        ["/database", "/database/erasure", "/database/identity", "/postgresql"];

    public static IReadOnlyList<string> Roots(SecretConsumer consumer) => consumer switch
    {
        SecretConsumer.Api => Array.AsReadOnly(ApiRoots),
        SecretConsumer.Bff => Array.AsReadOnly(BffRoots),
        SecretConsumer.Combined => Array.AsReadOnly<string>([.. ApiRoots, .. BffRoots]),
        SecretConsumer.Migration => Array.AsReadOnly(MigrationRoots),
        _ => throw new InvalidOperationException("secret_consumer_invalid")
    };

    public static bool AllowsPath(SecretConsumer consumer, string? path) =>
        path is not null && Roots(consumer).Contains(path, StringComparer.Ordinal);

    public static void AdmitPaths(SecretConsumer consumer, IEnumerable<string> paths)
    {
        string[] requested = paths.ToArray();
        if (requested.Length == 0 || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length
            || requested.Any(path => !AllowsPath(consumer, path)))
            throw new InvalidOperationException("secret_consumer_scope_invalid");
    }

    public static bool AllowsBinding(SecretConsumer consumer, SecretBinding binding, string environment)
    {
        SecretDefinition? definition = SecretDefinitionRegistry.TryGet(binding.SettingKey);
        if (definition is null || !AllowsPath(consumer, definition.DefaultInfisicalPath)
            || !definition.AllowedScopes.Contains(binding.Scope)
            || !definition.AllowedSources.Contains(binding.SourceType))
            return false;

        if (binding.SourceType == SecretSourceType.Infisical)
            return string.Equals(binding.InfisicalEnvironment, environment, StringComparison.Ordinal)
                && string.Equals(binding.InfisicalPath, definition.DefaultInfisicalPath, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(binding.InfisicalKey)
                && (!(definition.DefaultInfisicalPath.StartsWith("/runtime/", StringComparison.Ordinal)
                        || IsProtectedEnvironmentName(binding.InfisicalKey))
                    || string.Equals(binding.InfisicalKey, definition.DefaultInfisicalKey, StringComparison.Ordinal))
                && !binding.InfisicalKey.Contains("${", StringComparison.Ordinal)
                && !binding.InfisicalKey.Contains('/');

        // Tenant-specific environment coordinates remain supported for unrelated services.
        // They may not retarget an approved logical key to another consumer's credential.
        string? name = binding.EnvironmentVariableName;
        if (string.IsNullOrWhiteSpace(name) || IsProtectedEnvironmentName(name)
            || definition.DefaultInfisicalPath.StartsWith("/runtime/", StringComparison.Ordinal))
            return string.Equals(name, definition.DefaultEnvironmentVariableName, StringComparison.Ordinal)
                && AllowsPath(consumer, definition.DefaultInfisicalPath);
        return !name.Contains("${", StringComparison.Ordinal);
    }

    public static bool IsProtectedEnvironmentName(string name) =>
        name.StartsWith("KEYCLOAK", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CERBOS", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("INFISICAL_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("APPHOST_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("SecretProvider", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DATA_PROTECTION_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("EVENT_DP_", StringComparison.OrdinalIgnoreCase)
        || name is "GOOGLE_CLIENT_SECRET" or "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS";

    internal static void AdmitEntry(string path, string key)
    {
        if (IsProtectedEnvironmentName(key) && !path.StartsWith("/runtime/", StringComparison.Ordinal))
            throw new InvalidOperationException("secret_authority_invalid");
    }
}
