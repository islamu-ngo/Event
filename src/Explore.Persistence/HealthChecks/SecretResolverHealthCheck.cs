namespace Explore.Persistence.HealthChecks;

using Explore.Application.Contracts.Secrets;
using Explore.Persistence.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Verifies the secret resolver pipeline can initialize.
/// </summary>
public sealed class SecretResolverHealthCheck : IHealthCheck
{
    private readonly ISecretResolver _resolver;
    private readonly ISecretAuthorityStatusReader _authority;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecretResolverHealthCheck> _logger;

    public SecretResolverHealthCheck(
        ISecretResolver resolver,
        ISecretAuthorityStatusReader authority,
        IConfiguration configuration,
        ILogger<SecretResolverHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _authority = authority;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        PrimaryDatabaseConnectionOptions database;
        try
        {
            database = PrimaryDatabaseConfiguration.BindRuntime(_configuration);
        }
        catch (Exception)
        {
            _logger.LogError("secret_configuration_invalid");
            return HealthCheckResult.Unhealthy(
                description: "Secret resolver configuration is unavailable.",
                data: new Dictionary<string, object> { ["databaseConfiguration"] = "invalid" });
        }

        try
        {
            var authority = await _authority.ReadAsync(cancellationToken).ConfigureAwait(false);
            _ = _resolver;
            return authority.Status != "configured"
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
        catch (Exception)
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
