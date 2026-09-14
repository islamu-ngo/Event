namespace Explore.Secrets.Extensions;

using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring secret sources in IConfigurationBuilder.
/// </summary>
public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Adds Infisical as a configuration source.
    /// Secrets are loaded from Infisical and made available through IConfiguration.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="configuration">Configuration containing non-secret Infisical path selection.</param>
    /// <param name="configure">Optional action to configure additional options.</param>
    /// <param name="environmentName">Optional hosting environment name (e.g. Development, Production).</param>
    /// <returns>The configuration builder for chaining.</returns>
    /// <remarks>
    /// Bootstrap credentials are read directly from the process environment through
    /// <c>SecretProvider__Infisical__*</c> or the documented <c>INFISICAL_*</c> inputs,
    /// or from local User Secrets / bootstrap configuration in Development/Testing environments.
    /// In Production, merged configuration providers are never credential authorities.
    /// </remarks>
    public static IConfigurationBuilder AddInfisical(
        this IConfigurationBuilder builder,
        IConfiguration configuration,
        Action<InfisicalConfigurationSource>? configure = null,
        string? environmentName = null)
    {
        var resolvedEnv = environmentName
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Production;

        var projectId = ReadBootstrapValue("ProjectId", "INFISICAL_PROJECT_ID", configuration, resolvedEnv);
        var clientId = ReadBootstrapValue("ClientId", "INFISICAL_CLIENT_ID", configuration, resolvedEnv);
        var clientSecret = ReadBootstrapValue("ClientSecret", "INFISICAL_CLIENT_SECRET", configuration, resolvedEnv);
        var url = ReadBootstrapValue("Url", "INFISICAL_URL", configuration, resolvedEnv);
        var environment = ReadBootstrapValue("Environment", "INFISICAL_ENV", configuration, resolvedEnv);

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

    internal static string? ReadBootstrapValue(
        string name,
        string flatName,
        IConfiguration configuration,
        string environmentName)
    {
        var envVal = Environment.GetEnvironmentVariable($"SecretProvider__Infisical__{name}")
            ?? Environment.GetEnvironmentVariable(flatName);
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        if (SecretAuthorityConfiguration.IsDevelopmentOrTesting(environmentName))
        {
            var configVal = configuration[$"SecretProvider:Infisical:{name}"]
                ?? configuration[$"Infisical:{name}"]
                ?? configuration[flatName]
                ?? configuration[$"SecretProvider__Infisical__{name}"];
            if (!string.IsNullOrWhiteSpace(configVal))
            {
                return configVal;
            }

            var userSecrets = SecretAuthorityConfiguration.BuildUserSecrets();
            var userSecretsVal = userSecrets[$"SecretProvider:Infisical:{name}"]
                ?? userSecrets[$"Infisical:{name}"]
                ?? userSecrets[flatName]
                ?? userSecrets[$"SecretProvider__Infisical__{name}"];
            if (!string.IsNullOrWhiteSpace(userSecretsVal))
            {
                return userSecretsVal;
            }
        }

        return null;
    }
}
