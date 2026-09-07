using Explore.Domain.Enums;
using SecretSourceTypeEnum = Explore.Domain.Enums.SecretSourceType;

namespace Explore.Domain.Secrets;

public partial class SecretBinding
{
    /// <summary>
    /// Creates a binding that references a secret stored in Infisical.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown setting key, disallowed scope/source, or scope/scopeId inconsistency.</exception>
    public static SecretBinding CreateInfisical(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string environment,
        string path,
        string key,
        bool isLocked = false,
        string? qualifier = null)
    {
        var definition = GetValidDefinition(settingKey, scope, scopeId, SecretSourceTypeEnum.Infisical);

        if (string.IsNullOrWhiteSpace(environment))
            throw new ArgumentException("Infisical environment is required.", nameof(environment));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Infisical path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Infisical key is required.", nameof(key));
        _ = definition; // definition accepted by the check above; reference here keeps factory verbose-documented.

        return new SecretBinding
        {
            SettingKey = settingKey,
            Qualifier = NormalizeQualifier(qualifier),
            Scope = scope,
            ScopeId = scopeId,
            SourceType = SecretSourceTypeEnum.Infisical,
            InfisicalEnvironment = environment,
            InfisicalPath = path,
            InfisicalKey = key,
            IsLocked = isLocked,
        };
    }

    /// <summary>
    /// Creates a binding that points at a process environment variable.
    /// </summary>
    public static SecretBinding CreateEnvironmentVariable(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string variableName,
        bool isLocked = false,
        string? qualifier = null)
    {
        GetValidDefinition(settingKey, scope, scopeId, SecretSourceTypeEnum.EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(variableName))
            throw new ArgumentException("Environment variable name is required.", nameof(variableName));

        return new SecretBinding
        {
            SettingKey = settingKey,
            Qualifier = NormalizeQualifier(qualifier),
            Scope = scope,
            ScopeId = scopeId,
            SourceType = SecretSourceTypeEnum.EnvironmentVariable,
            EnvironmentVariableName = variableName,
            IsLocked = isLocked,
        };
    }

    /// <summary>
    /// Switches this binding to point at an Infisical reference, resetting all competing metadata
    /// and clearing the last-validation state so re-validation is forced.
    /// </summary>
    public void SwitchToInfisical(string environment, string path, string key)
    {
        GetValidDefinition(SettingKey, Scope, ScopeId, SecretSourceTypeEnum.Infisical);

        if (string.IsNullOrWhiteSpace(environment))
            throw new ArgumentException("Infisical environment is required.", nameof(environment));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Infisical path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Infisical key is required.", nameof(key));

        ClearMetadata();
        SourceType = SecretSourceTypeEnum.Infisical;
        InfisicalEnvironment = environment;
        InfisicalPath = path;
        InfisicalKey = key;
        ResetValidation();
    }

    /// <summary>
    /// Switches this binding to reference an environment variable, resetting all competing metadata
    /// and clearing the last-validation state.
    /// </summary>
    public void SwitchToEnvironmentVariable(string variableName)
    {
        GetValidDefinition(SettingKey, Scope, ScopeId, SecretSourceTypeEnum.EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(variableName))
            throw new ArgumentException("Environment variable name is required.", nameof(variableName));

        ClearMetadata();
        SourceType = SecretSourceTypeEnum.EnvironmentVariable;
        EnvironmentVariableName = variableName;
        ResetValidation();
    }

    /// <summary>
    /// Records the outcome of a validation attempt. Error messages must be pre-sanitised by the caller
    /// — they are rendered in the admin UI and must NOT contain secret values.
    /// </summary>
    public void RecordValidation(SecretValidationResult result, Guid? validatedBy, string? sanitisedError = null)
    {
        if (result == SecretValidationResult.Failure && string.IsNullOrWhiteSpace(sanitisedError))
            throw new ArgumentException("A sanitised error message is required when recording a failure.", nameof(sanitisedError));

        LastValidationResult = result;
        LastValidatedAt = DateTime.UtcNow;
        LastValidatedBy = validatedBy;
        LastValidationError = result == SecretValidationResult.Failure ? sanitisedError : null;
    }

    /// <summary>
    /// Validates the tuple (settingKey, scope, scopeId, sourceType) against the registry.
    /// Throws on the first violation with a message identifying the offending rule.
    /// </summary>
    private static SecretDefinition GetValidDefinition(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        SecretSourceType sourceType)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
            throw new ArgumentException("Setting key is required.", nameof(settingKey));

        var definition = SecretDefinitionRegistry.TryGet(settingKey)
            ?? throw new ArgumentException(
                $"Unknown secret key '{settingKey}'. Add a definition to SecretDefinitionRegistry first.",
                nameof(settingKey));

        if (!definition.AllowedScopes.Contains(scope))
            throw new ArgumentException(
                $"Scope '{scope}' is not allowed for secret '{settingKey}'. " +
                $"Allowed: {string.Join(", ", definition.AllowedScopes)}.",
                nameof(scope));

        switch (scope)
        {
            case SecretScope.Instance when scopeId.HasValue:
                throw new ArgumentException(
                    "Instance-scoped bindings must have a null scopeId.", nameof(scopeId));
            case SecretScope.Tenant when !scopeId.HasValue:
                throw new ArgumentException(
                    "Tenant-scoped bindings require a non-null scopeId (the tenant id).", nameof(scopeId));
        }

        if (!definition.AllowedSources.Contains(sourceType))
        {
            throw new ArgumentException(
                $"Source '{sourceType}' is not allowed for secret '{settingKey}'. " +
                $"Allowed: {string.Join(", ", definition.AllowedSources)}.",
                nameof(sourceType));
        }

        return definition;
    }

    private static string NormalizeQualifier(string? qualifier)
    {
        string normalized = qualifier?.Trim() ?? string.Empty;
        return normalized.Length <= 128
            ? normalized
            : throw new ArgumentException("Secret binding qualifier must be at most 128 characters.", nameof(qualifier));
    }

    /// <summary>Clears every metadata field; use before switching SourceType.</summary>
    private void ClearMetadata()
    {
        InfisicalEnvironment = null;
        InfisicalPath = null;
        InfisicalKey = null;
        EnvironmentVariableName = null;
    }

    /// <summary>Resets validation state to NotValidated; switching source always forces a re-validate.</summary>
    private void ResetValidation()
    {
        LastValidationResult = SecretValidationResult.NotValidated;
        LastValidationError = null;
        LastValidatedAt = null;
        LastValidatedBy = null;
    }
}
