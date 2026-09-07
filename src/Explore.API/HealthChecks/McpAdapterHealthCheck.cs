using Explore.API.Configuration;
using Explore.API.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class McpAdapterHealthCheck(
    IOptions<McpAdapterSettings> options,
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var scope = scopeFactory.CreateScope();
        var runtimeStateService = scope.ServiceProvider.GetRequiredService<IMcpRuntimeStateService>();
        var state = await runtimeStateService.GetAsync(cancellationToken: cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["enabled"] = state.EffectiveEnabled,
            ["startupEnabled"] = state.StartupEnabled,
            ["runtimeEnabled"] = state.RuntimeEnabled,
            ["endpointPath"] = settings.EndpointPath,
            ["stateless"] = settings.Stateless,
            ["legacySseStartupCeiling"] = state.StartupLegacySseCeiling,
            ["legacySseRuntimeRequested"] = state.RuntimeLegacySseRequested,
            ["legacySseRuntimeEnabled"] = state.LegacySseRuntimeEnabled
        };

        if (!state.StartupEnabled)
        {
            return HealthCheckResult.Degraded(
                "MCP adapter is disabled by startup configuration.",
                data: data);
        }

        if (!state.RuntimeEnabled)
        {
            return HealthCheckResult.Degraded(
                "MCP adapter is disabled by runtime governance.",
                data: data);
        }

        return HealthCheckResult.Healthy(
            "MCP adapter is enabled with stateless Streamable HTTP transport.",
            data);
    }
}
