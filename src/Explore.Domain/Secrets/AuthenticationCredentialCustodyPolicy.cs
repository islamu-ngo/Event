using Explore.Domain.Enums;

namespace Explore.Domain.Secrets;

/// <summary>
/// Exact Google/Keycloak runtime reference ownership. This structural predicate
/// neither certifies selected-provider access nor acquires credential contents.
/// </summary>
public static class AuthenticationCredentialCustodyPolicy
{
    public static bool IsRuntimeCredentialKey(string settingKey) =>
        settingKey is SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
            or SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret;

    public static bool HasCanonicalRuntimeReference(SecretBinding binding)
    {
        if (!IsRuntimeCredentialKey(binding.SettingKey)
            || binding.SettingScopeId != (int)ConfigurationScopeEnum.Instance
            || binding.ScopeId is not null || binding.Qualifier != string.Empty)
            return false;
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(binding.SettingKey);
        return binding.SourceType switch
        {
            SecretSourceType.EnvironmentVariable =>
                binding.EnvironmentVariableName == definition.DefaultEnvironmentVariableName,
            SecretSourceType.Infisical =>
                !string.IsNullOrWhiteSpace(binding.InfisicalEnvironment)
                && binding.InfisicalPath == definition.DefaultInfisicalPath
                && binding.InfisicalKey == definition.DefaultInfisicalKey,
            _ => false
        };
    }
}
