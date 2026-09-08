using Explore.Secrets.Abstractions;
using Explore.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Secrets.Configuration;

public static class SecretAuthorityConfiguration
{
    public static SecretProviderType GetRequiredProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured = configuration[$"{SecretProviderOptions.SectionName}:Provider"]
            ?? configuration["SECRET_PROVIDER"];

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
        SecretProviderType provider = GetRequiredProvider(bootstrapConfiguration);
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
        });
        return PreserveProviderSelection(builder.Build(), provider);
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
        SecretProviderType provider) =>
        new ConfigurationBuilder()
            .AddConfiguration(authority)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SecretProviderOptions.SectionName}:Provider"] = provider.ToString(),
                ["SECRET_PROVIDER"] = null,
            })
            .Build();

    internal static void EnsureUserSecretsEnvironment(string environmentName)
    {
        if (!string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("secret_authority_user_secrets_environment_invalid");
        }
    }
}
