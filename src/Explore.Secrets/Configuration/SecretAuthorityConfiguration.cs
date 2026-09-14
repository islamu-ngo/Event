using Explore.Secrets.Abstractions;
using Explore.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Secrets.Configuration;

public static class SecretAuthorityConfiguration
{
    public static SecretProviderType GetRequiredProvider(
        IConfiguration configuration,
        string? environmentName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured = configuration[$"{SecretProviderOptions.SectionName}:Provider"]
            ?? configuration["SECRET_PROVIDER"];

        string env = environmentName ?? GetEnvironmentName(configuration);
        if (string.IsNullOrEmpty(configured) && IsDevelopmentOrTesting(env))
        {
            var userSecrets = BuildUserSecrets();
            configured = userSecrets[$"{SecretProviderOptions.SectionName}:Provider"]
                ?? userSecrets["SECRET_PROVIDER"];

            if (string.IsNullOrEmpty(configured)
                && (!string.IsNullOrEmpty(userSecrets["Infisical:ClientId"])
                    || !string.IsNullOrEmpty(userSecrets["SecretProvider:Infisical:ClientId"])
                    || !string.IsNullOrEmpty(userSecrets["INFISICAL_CLIENT_ID"])
                    || !string.IsNullOrEmpty(configuration["Infisical:ClientId"])
                    || !string.IsNullOrEmpty(configuration["SecretProvider:Infisical:ClientId"])
                    || !string.IsNullOrEmpty(configuration["INFISICAL_CLIENT_ID"])))
            {
                configured = nameof(SecretProviderType.Infisical);
            }
        }

        if (!Enum.TryParse(configured, ignoreCase: true, out SecretProviderType provider)
            || provider is not (
                SecretProviderType.Environment
                or SecretProviderType.Infisical
                or SecretProviderType.UserSecrets))
        {
            throw new InvalidOperationException(
                "SecretProvider:Provider must explicitly select Environment, Infisical, or UserSecrets.");
        }

        return provider;
    }

    public static IConfiguration Build(
        IConfiguration bootstrapConfiguration,
        string environmentName,
        params string[] infisicalPaths)
    {
        SecretProviderType provider = GetRequiredProvider(bootstrapConfiguration, environmentName);
        if (provider == SecretProviderType.Environment)
        {
            IConfiguration environment = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            return PreserveProviderSelection(environment, provider);
        }

        if (provider == SecretProviderType.UserSecrets)
        {
            EnsureUserSecretsEnvironment(environmentName);
            return PreserveProviderSelection(BuildUserSecrets(), provider);
        }

        var builder = new ConfigurationBuilder();
        builder.AddInfisical(bootstrapConfiguration, source =>
        {
            source.Paths.Clear();
            source.Paths.AddRange(infisicalPaths);
            source.ThrowOnFirstLoadFailure = true;
        }, environmentName);
        return PreserveProviderSelection(builder.Build(), provider, bootstrapConfiguration, environmentName);
    }

    public static string GetEnvironmentName(IConfiguration configuration) =>
        configuration["DOTNET_ENVIRONMENT"]
        ?? configuration["ASPNETCORE_ENVIRONMENT"]
        ?? Environments.Production;

    internal static IConfiguration BuildUserSecrets() =>
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(SecretAuthorityConfiguration).Assembly, optional: true, reloadOnChange: false)
            .Build();

    internal static IConfiguration PreserveProviderSelection(
        IConfiguration authority,
        SecretProviderType provider,
        IConfiguration? bootstrapConfiguration = null,
        string? environmentName = null)
    {
        var entries = new Dictionary<string, string?>
        {
            [$"{SecretProviderOptions.SectionName}:Provider"] = provider.ToString(),
            ["SECRET_PROVIDER"] = null,
        };

        if (provider == SecretProviderType.Infisical && bootstrapConfiguration is not null)
        {
            string env = environmentName ?? GetEnvironmentName(bootstrapConfiguration);
            entries[$"{SecretProviderOptions.SectionName}:Infisical:Url"] =
                ConfigurationBuilderExtensions.ReadBootstrapValue("Url", "INFISICAL_URL", bootstrapConfiguration, env);
            entries[$"{SecretProviderOptions.SectionName}:Infisical:ProjectId"] =
                ConfigurationBuilderExtensions.ReadBootstrapValue("ProjectId", "INFISICAL_PROJECT_ID", bootstrapConfiguration, env);
            entries[$"{SecretProviderOptions.SectionName}:Infisical:ClientId"] =
                ConfigurationBuilderExtensions.ReadBootstrapValue("ClientId", "INFISICAL_CLIENT_ID", bootstrapConfiguration, env);
            entries[$"{SecretProviderOptions.SectionName}:Infisical:ClientSecret"] =
                ConfigurationBuilderExtensions.ReadBootstrapValue("ClientSecret", "INFISICAL_CLIENT_SECRET", bootstrapConfiguration, env);
            entries[$"{SecretProviderOptions.SectionName}:Infisical:Environment"] =
                ConfigurationBuilderExtensions.ReadBootstrapValue("Environment", "INFISICAL_ENV", bootstrapConfiguration, env);
        }

        return new ConfigurationBuilder()
            .AddConfiguration(authority)
            .AddInMemoryCollection(entries)
            .Build();
    }

    internal static void EnsureUserSecretsEnvironment(string environmentName)
    {
        if (!IsDevelopmentOrTesting(environmentName))
        {
            throw new InvalidOperationException("secret_authority_user_secrets_environment_invalid");
        }
    }

    public static bool IsDevelopmentOrTesting(string environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
}
