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
            using var userSecretsOwner = (IDisposable)userSecrets;
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
        params string[] infisicalPaths) =>
        Build(bootstrapConfiguration, environmentName, SecretConsumer.Api, infisicalPaths);

    public static IConfiguration Build(
        IConfiguration bootstrapConfiguration,
        string environmentName,
        SecretConsumer consumer,
        params string[] infisicalPaths)
    {
        SecretProviderType provider = GetRequiredProvider(bootstrapConfiguration, environmentName);
        if (provider == SecretProviderType.Environment)
        {
            IConfiguration environment = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            using var environmentOwner = (IDisposable)environment;
            return PreserveProviderSelection(environment, provider, consumer: consumer);
        }

        if (provider == SecretProviderType.UserSecrets)
        {
            EnsureUserSecretsEnvironment(environmentName);
            IConfiguration userSecrets = BuildUserSecrets();
            using var userSecretsOwner = (IDisposable)userSecrets;
            return PreserveProviderSelection(userSecrets, provider, consumer: consumer);
        }

        SecretAcquisitionPolicy.AdmitPaths(consumer, infisicalPaths);
        var builder = new ConfigurationBuilder();
        builder.AddInfisical(bootstrapConfiguration, source =>
        {
            source.Paths.Clear();
            source.Paths.AddRange(infisicalPaths);
            source.Consumer = consumer;
            source.ThrowOnFirstLoadFailure = true;
        }, environmentName);
        var source = (InfisicalConfigurationSource)builder.Sources.Single();
        IConfiguration authority = builder.Build();
        using var authorityOwner = (IDisposable)authority;
        return PreserveProviderSelection(authority, provider, source, consumer);
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
        InfisicalConfigurationSource? infisicalSource = null,
        SecretConsumer consumer = SecretConsumer.Api)
    {
        const string prefix = "SecretProvider:Infisical";
        var runtime = authority.GetSection(prefix).AsEnumerable()
            .ToDictionary(pair => pair.Key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        runtime[$"{SecretProviderOptions.SectionName}:Provider"] = provider.ToString();
        runtime[$"{SecretProviderOptions.SectionName}:Consumer"] = consumer.ToString();
        runtime["SECRET_PROVIDER"] = null;
        if (infisicalSource is not null)
        {
            // Reuse the source that actually authenticated, never values returned by the vault
            // or a second bootstrap read that could select a different runtime authority.
            runtime[$"{prefix}:Url"] = infisicalSource.Url;
            runtime[$"{prefix}:ProjectId"] = infisicalSource.ProjectId;
            runtime[$"{prefix}:ClientId"] = infisicalSource.ClientId;
            runtime[$"{prefix}:ClientSecret"] = infisicalSource.ClientSecret;
            runtime[$"{prefix}:Environment"] = infisicalSource.Environment;
            for (int index = 0; index < infisicalSource.Paths.Count; index++)
                runtime[$"{prefix}:Paths:{index}"] = infisicalSource.Paths[index];
        }
        // Preserve empty declarations that must block credential-alias fallback.
        return new ConfigurationBuilder()
            .AddInMemoryCollection(authority.AsEnumerable())
            .AddInMemoryCollection(runtime)
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
