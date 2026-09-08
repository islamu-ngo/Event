using Explore.Blazor.Configuration;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.Extensions;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddInfisical(
        this IConfigurationBuilder builder,
        IConfiguration configuration,
        Action<InfisicalConfigurationSource>? configure = null)
    {
        var projectId = ReadBootstrapValue("ProjectId", "INFISICAL_PROJECT_ID");
        var clientId = ReadBootstrapValue("ClientId", "INFISICAL_CLIENT_ID");
        var clientSecret = ReadBootstrapValue("ClientSecret", "INFISICAL_CLIENT_SECRET");
        var url = ReadBootstrapValue("Url", "INFISICAL_URL");
        var environment = ReadBootstrapValue("Environment", "INFISICAL_ENV");

        if (string.IsNullOrEmpty(projectId)
            || string.IsNullOrEmpty(clientId)
            || string.IsNullOrEmpty(clientSecret)
            || string.IsNullOrEmpty(url)
            || string.IsNullOrEmpty(environment))
        {
            throw new InvalidOperationException(
                "Infisical authority requires an explicit URL, environment, project, and universal-auth credentials.");
        }

        var source = new InfisicalConfigurationSource
        {
            Url = url,
            ProjectId = projectId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            Environment = environment,
        };

        var paths = configuration.GetSection("SecretProvider:Infisical:Paths").Get<List<string>>();
        if (paths is { Count: > 0 })
        {
            source.Paths.Clear();
            source.Paths.AddRange(paths);
        }

        configure?.Invoke(source);
        source.Url = url;
        source.ProjectId = projectId;
        source.ClientId = clientId;
        source.ClientSecret = clientSecret;
        source.Environment = environment;
        return builder.Add(source);
    }

    /// <summary>
    /// Adds Infisical secrets and maps them to canonical .NET configuration keys for Blazor Server.
    /// </summary>
    public static void AddSecretAuthorityConfiguration(
        this IConfigurationBuilder configBuilder,
        string environmentName)
    {
        var bootstrapConfig = configBuilder.Build();
        string? configuredProvider = bootstrapConfig["SecretProvider:Provider"]
            ?? bootstrapConfig["SECRET_PROVIDER"];
        if (string.Equals(configuredProvider, "Environment", StringComparison.OrdinalIgnoreCase))
        {
            ApplyBlazorMapping(
                configBuilder,
                new ConfigurationBuilder().AddEnvironmentVariables().Build(),
                fromInfisical: false);
            return;
        }

        if (string.Equals(configuredProvider, "UserSecrets", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("secret_authority_user_secrets_environment_invalid");
            }

            ApplyBlazorMapping(
                configBuilder,
                new ConfigurationBuilder()
                    .AddUserSecrets("event-shared-secrets", reloadOnChange: false)
                    .Build(),
                fromInfisical: false);
            return;
        }

        if (!string.Equals(configuredProvider, "Infisical", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SecretProvider:Provider must explicitly select Environment, Infisical, or UserSecrets.");
        }

        var authorityBuilder = new ConfigurationBuilder();
        authorityBuilder.AddInfisical(bootstrapConfig, source =>
        {
            source.Paths.Clear();
            source.Paths.AddRange(["/keycloak", "/blazor", "/atproto"]);
            source.ThrowOnFirstLoadFailure = true;
        });
        ApplyBlazorMapping(configBuilder, authorityBuilder.Build(), fromInfisical: true);
    }

    /// <summary>
    /// Maps Infisical secret names to .NET configuration keys for Blazor Server.
    /// </summary>
    /// <remarks>
    /// Canonical Infisical keys:
    ///   /keycloak: KEYCLOAK_ENDPOINT, KEYCLOAK_REALM, KEYCLOAK_CLIENT_ID, KEYCLOAK_BLAZOR_CLIENT_SECRET
    ///   /blazor:   API_ENDPOINT, GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET
    /// </remarks>
    private static void ApplyBlazorMapping(
        IConfigurationBuilder configBuilder,
        IConfiguration config,
        bool fromInfisical)
    {
        var rawRealm = config[fromInfisical ? "Keycloak:Realm" : "KEYCLOAK_REALM"];
        var rawKeycloakClientId = config[fromInfisical ? "Keycloak:ClientId" : "KEYCLOAK_CLIENT_ID"];
        var rawClientSecret = config[fromInfisical ? "Keycloak:BlazorClientSecret" : "KEYCLOAK_BLAZOR_CLIENT_SECRET"];
        var rawGoogleClientId = config[fromInfisical ? "Blazor:GoogleClientId" : "GOOGLE_CLIENT_ID"];
        var rawGoogleClientSecret = config[fromInfisical ? "Blazor:GoogleClientSecret" : "GOOGLE_CLIENT_SECRET"];
        var rawApiUrl = config[fromInfisical ? "Blazor:ApiEndpoint" : "API_ENDPOINT"];
        var rawAtprotoOAuthClientPrivateJwks = config[
            fromInfisical ? "Atproto:OauthClientPrivateJwks" : "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"];
        var hasAspireApiReference =
            !string.IsNullOrWhiteSpace(GetAspireApiReference(config, "http"))
            || !string.IsNullOrWhiteSpace(GetAspireApiReference(config, "https"));
        var baseUrl = config[fromInfisical ? "Keycloak:Endpoint" : "KEYCLOAK_ENDPOINT"];

        var hasKeycloakInput =
            !string.IsNullOrWhiteSpace(baseUrl)
            || !string.IsNullOrWhiteSpace(rawRealm)
            || !string.IsNullOrWhiteSpace(rawKeycloakClientId)
            || !string.IsNullOrWhiteSpace(rawClientSecret);

        string? keycloakAuthority = null;
        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(rawRealm))
        {
            keycloakAuthority = $"{baseUrl.TrimEnd('/')}/realms/{rawRealm}";
        }

        var keycloakClientId = rawKeycloakClientId;
        if (string.IsNullOrWhiteSpace(keycloakClientId) && !string.IsNullOrWhiteSpace(keycloakAuthority))
        {
            keycloakClientId = "islamu-event-blazor";
        }

        var metadataAddress = string.IsNullOrWhiteSpace(keycloakAuthority)
            ? null
            : $"{keycloakAuthority}/.well-known/openid-configuration";

        var mappedConfig = new Dictionary<string, string?>
        {
            ["Keycloak:ClientSecret"] = rawClientSecret,
            ["Google:ClientSecret"] = rawGoogleClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(rawAtprotoOAuthClientPrivateJwks))
        {
            mappedConfig[AtprotoClientKeyProvider.ConfigurationKey] = rawAtprotoOAuthClientPrivateJwks;
        }

        static void TrySet(IDictionary<string, string?> dict, IConfiguration root, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.IsNullOrEmpty(root[key]))
                return;
            dict[key] = value;
        }

        // Keycloak
        if (hasKeycloakInput)
        {
            TrySet(mappedConfig, config, "Keycloak:Realm", rawRealm);
            TrySet(mappedConfig, config, "Keycloak:Authority", keycloakAuthority);
            TrySet(mappedConfig, config, "Keycloak:MetadataAddress", metadataAddress);
            TrySet(mappedConfig, config, "Keycloak:ClientId", keycloakClientId);
            TrySet(mappedConfig, config, "Keycloak:RequireHttpsMetadata", "true");
        }

        // Google
        TrySet(mappedConfig, config, "Google:ClientId", rawGoogleClientId);

        // API
        if (!hasAspireApiReference && !string.IsNullOrEmpty(rawApiUrl))
        {
            TrySet(mappedConfig, config, "ExploreApi:BaseUrl", rawApiUrl);
        }

        configBuilder.AddInMemoryCollection(mappedConfig);
    }

    private static string? ReadBootstrapValue(string name, string flatName) =>
        Environment.GetEnvironmentVariable($"SecretProvider__Infisical__{name}")
        ?? Environment.GetEnvironmentVariable(flatName);

    private static string? GetAspireApiReference(IConfiguration config, string scheme) =>
        config[$"services:explore-api:{scheme}:0"]
        ?? config[$"services__explore-api__{scheme}__0"];
}
