using Explore.API.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
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
        var reader = scope.ServiceProvider.GetRequiredService<IInstanceSystemSettingsReader>();
        var keys = McpSettingGroup.SettingKeys.ToArray();
        var stored = (await reader.ReadAsync(keys, cancellationToken))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var runtime = new McpSettingGroup();
        runtime.Populate(keys.Select(key => HierarchicalSettingMerge.Resolve(key, stored))
            .OfType<ResolvedSetting>().ToDictionary(value => value.Key, StringComparer.Ordinal));

        var data = new Dictionary<string, object>
        {
            ["enabled"] = settings.Enabled && runtime.Enabled,
            ["startupEnabled"] = settings.Enabled,
            ["runtimeEnabled"] = runtime.Enabled,
            ["endpointPath"] = settings.EndpointPath,
            ["stateless"] = settings.Stateless,
            ["legacySseStartupCeiling"] = settings.EnableLegacySse,
            ["legacySseRuntimeRequested"] = runtime.EnableLegacySse,
            ["legacySseRuntimeEnabled"] = false
        };

        if (!settings.Enabled)
        {
            return HealthCheckResult.Degraded(
                "MCP adapter is disabled by startup configuration.",
                data: data);
        }

        if (!runtime.Enabled)
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
