using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class AiRetentionCleanupHealthCheck(
    IOptions<AiRetentionCleanupSettings> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = settings.Enabled,
            ["dryRun"] = settings.DryRun,
            ["initialDelaySeconds"] = settings.InitialDelaySeconds,
            ["pollingIntervalMinutes"] = settings.PollingIntervalMinutes,
            ["maxTenantsPerPass"] = settings.MaxTenantsPerPass
        };

        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "AI retention cleanup is intentionally disabled.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            settings.DryRun
                ? "AI retention cleanup is enabled in dry-run mode."
                : "AI retention cleanup is enabled in redaction mode.",
            data));
    }
}
