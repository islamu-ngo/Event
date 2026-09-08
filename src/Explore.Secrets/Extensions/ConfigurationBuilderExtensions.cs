namespace Explore.Secrets.Extensions;

using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

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
    /// <returns>The configuration builder for chaining.</returns>
    /// <remarks>
    /// Bootstrap credentials are read directly from the process environment through
    /// <c>SecretProvider__Infisical__*</c> or the documented <c>INFISICAL_*</c> inputs.
    /// Merged configuration providers are never credential authorities.
    /// </remarks>
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

    private static string? ReadBootstrapValue(string name, string flatName) =>
        Environment.GetEnvironmentVariable($"SecretProvider__Infisical__{name}")
        ?? Environment.GetEnvironmentVariable(flatName);
}
