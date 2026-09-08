// Instantiates the appropriate provider type from SecretProviderOptions.

using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

/// <summary>
/// Factory for creating <see cref="ISecretProvider"/> instances.
/// Determines the provider type from configuration and instantiates appropriately.
/// </summary>
public sealed class SecretProviderFactory
{
    private readonly IOptions<SecretProviderOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UserSecretsAuthority? _userSecretsAuthority;

    public SecretProviderFactory(
        IOptions<SecretProviderOptions> options,
        ILoggerFactory loggerFactory,
        UserSecretsAuthority? userSecretsAuthority = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _userSecretsAuthority = userSecretsAuthority;
    }

    /// <summary>
    /// Creates a secret provider based on the configured provider type.
    /// </summary>
    /// <returns>An initialized secret provider instance.</returns>
    /// <exception cref="SecretProviderException">When the provider type is not supported.</exception>
    public ISecretProvider Create()
    {
        var options = _options.Value;
        var logger = _loggerFactory.CreateLogger<SecretProviderFactory>();

        logger.LogInformation(
            "Creating secret provider of type: {ProviderType}",
            options.Provider);

        return options.Provider switch
        {
            SecretProviderType.Environment => CreateEnvironmentProvider(),
            SecretProviderType.UserSecrets => CreateUserSecretsProvider(),
            SecretProviderType.Infisical => CreateInfisicalProvider(),
            _ => throw new SecretProviderException(
                $"Unsupported secret provider type: {options.Provider}",
                options.Provider,
                "Create",
                isTransient: false)
        };
    }

    private ISecretProvider CreateEnvironmentProvider()
    {
        var logger = _loggerFactory.CreateLogger<EnvironmentSecretProvider>();
        return new EnvironmentSecretProvider(logger);
    }

    private ISecretProvider CreateUserSecretsProvider()
    {
        var logger = _loggerFactory.CreateLogger<EnvironmentSecretProvider>();
        return new EnvironmentSecretProvider(
            logger,
            _userSecretsAuthority ?? throw new InvalidOperationException(
                "User Secrets authority is unavailable."));
    }

    private ISecretProvider CreateInfisicalProvider()
    {
        var logger = _loggerFactory.CreateLogger<InfisicalSecretProvider>();
        return new InfisicalSecretProvider(logger, _options);
    }

}
