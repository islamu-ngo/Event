using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.Configuration;

/// <summary>
/// Final project-process projection used by AppHost. Aspire's explicit resource
/// settings survive; ambient deployment secrets and sibling identities do not.
/// Provider containers are configured separately by the operator.
/// </summary>
public static class RuntimeSecretEnvironment
{
    public static void Apply(
        IDictionary<string, object> target,
        IConfiguration operatorInputs,
        IEnumerable<string> inheritedNames,
        SecretConsumer consumer,
        SecretProviderType provider)
    {
        // Empty values override parent inheritance. Removing a key would expose it again.
        foreach (string name in inheritedNames)
        {
            if (!target.ContainsKey(name) && !IsOperatingSystemVariable(name))
                target[name] = string.Empty;
        }

        foreach (string name in target.Keys.ToArray())
        {
            if (Forbidden(name, consumer)
                || provider == SecretProviderType.Infisical && IsDatabaseCoordinate(name))
                target[name] = string.Empty;
        }

        foreach (SecretDefinition definition in SecretDefinitionRegistry.All.Values)
        {
            if (!SecretAcquisitionPolicy.AllowsPath(consumer, definition.DefaultInfisicalPath))
                continue;
            string name = definition.DefaultEnvironmentVariableName;
            if (provider == SecretProviderType.Environment && operatorInputs[name] is { } value)
                target[name] = value;
            else if (provider == SecretProviderType.Infisical)
                target[name] = string.Empty;
        }

        if (consumer is SecretConsumer.Bff or SecretConsumer.Combined)
        {
            foreach (string name in new[] { "KEYCLOAK_ENDPOINT", "KEYCLOAK_REALM", "KEYCLOAK_CLIENT_ID",
                "KEYCLOAK_BLAZOR_CLIENT_ID", "GOOGLE_CLIENT_ID", "API_ENDPOINT", "AUTHENTICATION_PROVIDER",
                "AUTHENTICATION_CREDENTIAL_PROFILE_REVISION" })
                if (provider == SecretProviderType.Environment && operatorInputs[name] is { } value
                    && (!target.TryGetValue(name, out object? existing) || existing is string { Length: 0 }))
                    target[name] = value;
        }

        // These are read directly by the standalone DP loader; never ordinary vault configuration.
        string? partition = consumer switch
        {
            SecretConsumer.Api => "API",
            SecretConsumer.Bff => "BFF",
            SecretConsumer.Combined => "COMBINED",
            _ => null
        };
        if (partition is not null)
        {
            foreach (string suffix in new[] { "ADMISSION_SHA256", "MINIMUM_GENERATION", "RING", "ADMISSION" })
            {
                string name = $"EVENT_DP_{partition}_{suffix}";
                bool processInput = suffix is "ADMISSION_SHA256" or "MINIMUM_GENERATION";
                string? value = processInput ? Environment.GetEnvironmentVariable(name) : operatorInputs[name];
                if ((processInput || provider == SecretProviderType.Environment) && value is not null)
                    target[name] = value;
            }
        }

        target["SecretProvider__Provider"] = provider.ToString();
        target["SECRET_PROVIDER"] = provider.ToString();
        target["SecretProvider__Consumer"] = consumer.ToString();
        string[] fields = ["Url", "ProjectId", "ClientId", "ClientSecret", "Environment"];
        foreach (string field in fields)
            target[$"SecretProvider__Infisical__{field}"] = string.Empty;
        foreach (string name in new[] { "INFISICAL_URL", "INFISICAL_PROJECT_ID", "INFISICAL_CLIENT_ID", "INFISICAL_CLIENT_SECRET", "INFISICAL_ENV" })
            target[name] = string.Empty;
        if (provider != SecretProviderType.Infisical)
            return;

        string prefix = $"INFISICAL_{consumer.ToString().ToUpperInvariant()}";
        string clientId = Required(operatorInputs, $"{prefix}_CLIENT_ID");
        foreach (SecretConsumer other in Enum.GetValues<SecretConsumer>())
        {
            if (other != consumer && string.Equals(clientId,
                    operatorInputs[$"INFISICAL_{other.ToString().ToUpperInvariant()}_CLIENT_ID"], StringComparison.Ordinal))
                throw new InvalidOperationException("secret_consumer_identity_reused");
        }
        target["SecretProvider__Infisical__Url"] = Required(operatorInputs, "INFISICAL_URL");
        target["SecretProvider__Infisical__ProjectId"] = Required(operatorInputs, "INFISICAL_PROJECT_ID");
        target["SecretProvider__Infisical__Environment"] = Required(operatorInputs, "INFISICAL_ENV");
        target["SecretProvider__Infisical__ClientId"] = clientId;
        target["SecretProvider__Infisical__ClientSecret"] = Required(operatorInputs, $"{prefix}_CLIENT_SECRET");
    }

    private static string Required(IConfiguration inputs, string name) =>
        !string.IsNullOrWhiteSpace(inputs[name]) ? inputs[name]!
            : throw new InvalidOperationException("secret_consumer_identity_missing");

    private static bool Forbidden(string name, SecretConsumer consumer)
    {
        string normalized = name.Replace("__", "_", StringComparison.Ordinal).Replace(':', '_').ToUpperInvariant();
        if (normalized.StartsWith("INFISICAL_", StringComparison.Ordinal)
            || normalized.StartsWith("SECRETPROVIDER_INFISICAL_", StringComparison.Ordinal)
            || normalized.StartsWith("APPHOST_", StringComparison.Ordinal)
            || normalized.StartsWith("KEYCLOAK_ADMIN", StringComparison.Ordinal)
            || normalized.StartsWith("KEYCLOAK_DB_", StringComparison.Ordinal)
            || normalized.StartsWith("KC_", StringComparison.Ordinal)
            || normalized.StartsWith("CERBOS_ADMIN", StringComparison.Ordinal)
                && !normalized.StartsWith("CERBOS_ADMINAPI_ENDPOINTS_", StringComparison.Ordinal)
            || normalized.StartsWith("CERBOS_POSTGRES_", StringComparison.Ordinal)
            || normalized == "CERBOS_PG_URL")
            return true;
        if (normalized.StartsWith("EVENT_DP_", StringComparison.Ordinal))
        {
            string own = $"EVENT_DP_{consumer.ToString().ToUpperInvariant()}_";
            return !normalized.StartsWith(own, StringComparison.Ordinal);
        }
        if (consumer == SecretConsumer.Api || consumer == SecretConsumer.Migration)
            return normalized is "KEYCLOAK_BLAZOR_CLIENT_SECRET" or "KEYCLOAK_CLIENTSECRET"
                or "KEYCLOAK_CLIENT_SECRET" or "KEYCLOAK_BLAZORCLIENTSECRET"
                or "GOOGLE_CLIENT_SECRET" or "GOOGLE_CLIENTSECRET" or "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS";
        if (consumer == SecretConsumer.Bff)
            return normalized.StartsWith("DATABASE", StringComparison.Ordinal)
                || normalized.StartsWith("POSTGRES", StringComparison.Ordinal)
                || normalized.StartsWith("IDENTITYDATABASE", StringComparison.Ordinal)
                || normalized.StartsWith("IDENTITY_DATABASE", StringComparison.Ordinal)
                || normalized.StartsWith("ERASURE_DATABASE", StringComparison.Ordinal)
                || normalized.StartsWith("PRIVACYERASUREAUTHORITYDATABASE", StringComparison.Ordinal)
                || normalized is "KEYCLOAK_LIFECYCLE_CLIENT_SECRET" or "AUTHENTICATION_LOCAL_JWT_KEY";
        return false;
    }

    private static bool IsOperatingSystemVariable(string name) => name is
        "PATH" or "HOME" or "USER" or "TMPDIR" or "TMP" or "TEMP" or "LANG" or "LC_ALL"
        or "SystemRoot" or "SYSTEMROOT" or "WINDIR" or "DOTNET_ROOT"
        or "DOTNET_ROOT_X64" or "DOTNET_ROOT_ARM64";

    private static bool IsDatabaseCoordinate(string name) =>
        name.StartsWith("Database", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Database__Migrations__", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("IdentityDatabase", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("PrivacyErasureAuthorityDatabase", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("ERASURE_DATABASE_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("POSTGRESQL_", StringComparison.OrdinalIgnoreCase);
}
