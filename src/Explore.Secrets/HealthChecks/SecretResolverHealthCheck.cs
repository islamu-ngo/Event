namespace Explore.Secrets.HealthChecks;

using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Verifies the secret resolver pipeline can initialize.
/// </summary>
public sealed class SecretResolverHealthCheck : IHealthCheck
{
    private readonly ISecretResolver _resolver;
    private readonly IInfisicalClientFactory _infisicalFactory;
    private readonly IConfiguration _configuration;
    private readonly UserSecretsAuthority _userSecretsAuthority;
    private readonly ILogger<SecretResolverHealthCheck> _logger;

    public SecretResolverHealthCheck(
        ISecretResolver resolver,
        IInfisicalClientFactory infisicalFactory,
        IConfiguration configuration,
        UserSecretsAuthority userSecretsAuthority,
        ILogger<SecretResolverHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(infisicalFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(userSecretsAuthority);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _infisicalFactory = infisicalFactory;
        _configuration = configuration;
        _userSecretsAuthority = userSecretsAuthority;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        PrimaryDatabaseConnectionOptions database;
        SecretProviderType provider;
        try
        {
            database = PrimaryDatabaseConfiguration.BindRuntime(_configuration);
            provider = SecretAuthorityConfiguration.GetRequiredProvider(_configuration);
        }
#pragma warning disable CA1031 // Do not catch general exception types - boundary check
        catch (Exception)
#pragma warning restore CA1031
        {
            _logger.LogError("secret_configuration_invalid");
            return HealthCheckResult.Unhealthy(
                description: "Secret resolver configuration is unavailable.",
                data: new Dictionary<string, object> { ["databaseConfiguration"] = "invalid" });
        }

        try
        {
            if (provider is SecretProviderType.Environment or SecretProviderType.UserSecrets)
            {
                if (provider == SecretProviderType.UserSecrets)
                    _userSecretsAuthority.EnsureAllowed();

                _ = _resolver;
                return HealthCheckResult.Healthy(
                    "Secret resolver pipeline is available.",
                    new Dictionary<string, object>
                    {
                        ["databaseProvider"] = database.Provider.ToString(),
                        ["providerState"] = "available"
                    });
            }

            var client = await _infisicalFactory.GetClientAsync(cancellationToken).ConfigureAwait(false);
            _ = _resolver;
            return client is null
                ? HealthCheckResult.Unhealthy(
                    description: "Selected secret provider is unavailable.",
                    data: UnavailableData(database))
                : HealthCheckResult.Healthy(
                    "Secret resolver pipeline is available.",
                    new Dictionary<string, object>
                    {
                        ["databaseProvider"] = database.Provider.ToString(),
                        ["providerState"] = "available"
                    });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Provider boundary exposes a bounded health state.
        catch (Exception)
#pragma warning restore CA1031
        {
            _logger.LogError("secret_provider_unavailable");
            return HealthCheckResult.Unhealthy(
                description: "Selected secret provider is unavailable.",
                data: UnavailableData(database));
        }
    }

    private static Dictionary<string, object> UnavailableData(PrimaryDatabaseConnectionOptions database) => new()
    {
        ["databaseProvider"] = database.Provider.ToString(),
        ["providerState"] = "unavailable",
        ["remediation"] = "docs/TROUBLESHOOTING.md#secret-provider-unavailable"
    };
}
