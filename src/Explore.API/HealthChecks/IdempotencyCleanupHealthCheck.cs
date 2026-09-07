using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class IdempotencyCleanupHealthCheck(
    IOptions<IdempotencyCleanupSettings> options) : IHealthCheck
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
            ["batchSize"] = settings.BatchSize,
            ["expirationGraceHours"] = settings.ExpirationGraceHours
        };

        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Idempotency cleanup is intentionally disabled.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            settings.DryRun
                ? "Idempotency cleanup is enabled in dry-run mode."
                : "Idempotency cleanup is enabled in delete mode.",
            data));
    }
}
