using Explore.Domain.Enums;

namespace Explore.Domain.Secrets;

/// <summary>
/// Admits one already-selected binding against registry policy and the caller's exact
/// consumer roots. It neither selects a hierarchy winner nor authorizes a provider identity.
/// </summary>
public static class SecretBindingAdmission
{
    public static bool AllowsBinding(
        SecretBinding binding, IReadOnlyCollection<string> allowedRoots, string? infisicalEnvironment)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(allowedRoots);
        if (binding.SettingScopeId is not (1 or 2)
            || binding.SettingScopeId == 1 && binding.ScopeId is not null
            || binding.SettingScopeId == 2 && (binding.ScopeId is null || binding.ScopeId == Guid.Empty)
            || binding.Qualifier is null
            || binding.Qualifier.Length != 0 && string.IsNullOrWhiteSpace(binding.Qualifier)
            || string.IsNullOrWhiteSpace(binding.SettingKey))
            return false;

        SecretDefinition? definition = SecretDefinitionRegistry.TryGet(binding.SettingKey);
        if (definition is null
            || !allowedRoots.Contains(definition.DefaultInfisicalPath, StringComparer.Ordinal)
            || !definition.AllowedScopes.Contains(binding.Scope)
            || !definition.AllowedSources.Contains(binding.SourceType))
            return false;

        bool canonicalRuntime = definition.Key is SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
            or SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret
            or SecretDefinitionRegistry.Keys.Keycloak.LifecycleClientSecret
            or SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks;
        if (canonicalRuntime && (binding.Scope != SecretScope.Instance || binding.Qualifier.Length != 0
            || !string.Equals(binding.SettingKey, definition.Key, StringComparison.Ordinal)))
            return false;

        if (binding.SourceType == SecretSourceType.Infisical)
        {
            string? key = binding.InfisicalKey;
            return binding.EnvironmentVariableName is null
                && !string.IsNullOrWhiteSpace(infisicalEnvironment)
                && string.Equals(binding.InfisicalEnvironment, infisicalEnvironment, StringComparison.Ordinal)
                && string.Equals(binding.InfisicalPath, definition.DefaultInfisicalPath, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(key) && !IsAuthorityInput(key)
                && !key.Contains("${", StringComparison.Ordinal) && !key.Contains('/') && !key.Contains('\\')
                && (!(canonicalRuntime
                        || definition.DefaultInfisicalPath.StartsWith("/runtime/", StringComparison.Ordinal)
                        || IsProtectedEnvironmentName(key))
                    || string.Equals(key, definition.DefaultInfisicalKey, StringComparison.Ordinal));
        }

        if (binding.SourceType != SecretSourceType.EnvironmentVariable
            || binding.InfisicalEnvironment is not null || binding.InfisicalPath is not null
            || binding.InfisicalKey is not null)
            return false;

        string? name = binding.EnvironmentVariableName;
        return !string.IsNullOrWhiteSpace(name) && !IsAuthorityInput(name)
            && !name.Contains("${", StringComparison.Ordinal) && !name.Contains('/') && !name.Contains('\\')
            && (!(canonicalRuntime || IsProtectedEnvironmentName(name)
                    || definition.DefaultInfisicalPath.StartsWith("/runtime/", StringComparison.Ordinal))
                || string.Equals(name, definition.DefaultEnvironmentVariableName, StringComparison.Ordinal));
    }

    private static bool IsAuthorityInput(string name) =>
        string.Equals(name, "SECRET_PROVIDER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "ASPNETCORE_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("INFISICAL", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("APPHOST", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("SecretProvider", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DataProtection", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DATA_PROTECTION", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("EVENT_DP", StringComparison.OrdinalIgnoreCase);

    private static bool IsProtectedEnvironmentName(string name)
    {
        string configurationName = name.Replace("__", ":", StringComparison.Ordinal);
        return name.StartsWith("KEYCLOAK", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("CERBOS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "GOOGLE_CLIENT_SECRET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configurationName, "Google:ClientSecret", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configurationName, "Atproto:OauthClientPrivateJwks", StringComparison.OrdinalIgnoreCase);
    }
}
