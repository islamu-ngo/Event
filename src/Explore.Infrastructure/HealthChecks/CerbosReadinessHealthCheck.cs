using Explore.Application.Contracts.Services;
using Explore.Application.Utilities;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class CerbosReadinessHealthCheck(
    IAuthorizationProviderConfigurationService configurationService,
    IOptions<CerbosSettings> cerbosOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.ReadConfigurationAsync();
        var provider = configuration.Provider?.Trim().ToLowerInvariant() ?? "local";

        if (provider != "cerbos")
        {
            return HealthCheckResult.Healthy(
                "Cerbos readiness skipped because the instance authorization provider is local.",
                new Dictionary<string, object>
                {
                    ["provider"] = provider
                });
        }

        var endpoint = ResolveEndpoint(configuration.CerbosGrpcEndpoint, cerbosOptions.Value.GrpcEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return HealthCheckResult.Unhealthy(
                "Cerbos is the configured authorization provider, but no gRPC endpoint is configured.",
                data: new Dictionary<string, object>
                {
                    ["provider"] = provider
                });
        }

        var reachable = await configurationService.VerifyCerbosEndpointAsync(endpoint, cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["endpoint"] = endpoint
        };

        return reachable
            ? HealthCheckResult.Healthy("Cerbos PDP is reachable.", data)
            : HealthCheckResult.Unhealthy("Cerbos is the configured authorization provider, but the PDP is unreachable.", data: data);
    }

    private static string ResolveEndpoint(string? configuredEndpoint, string optionsEndpoint)
    {
        var rawEndpoint = string.IsNullOrWhiteSpace(configuredEndpoint)
            ? optionsEndpoint
            : configuredEndpoint;

        return GrpcEndpointNormalizer.Normalize(rawEndpoint);
    }
}
