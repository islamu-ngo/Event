using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class EmailDispatchRetentionCleanupHealthCheck(
    IOptions<EmailDispatchRetentionSettings> options) : IHealthCheck
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
            ["retentionDays"] = settings.RetentionDays,
            ["batchSize"] = settings.BatchSize,
            ["maxTenantsPerPass"] = settings.MaxTenantsPerPass,
            ["pollingIntervalMinutes"] = settings.PollingIntervalMinutes
        };

        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Email dispatch retention cleanup is intentionally disabled.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            settings.DryRun
                ? "Email dispatch retention cleanup is enabled in dry-run mode."
                : "Email dispatch retention cleanup is enabled in redaction mode.",
            data));
    }
}
