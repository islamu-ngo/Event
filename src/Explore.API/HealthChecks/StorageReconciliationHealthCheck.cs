using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class StorageReconciliationHealthCheck(
    IOptions<StorageReconciliationSettings> options) : IHealthCheck
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
            ["missingObjectQuarantineGraceHours"] = settings.MissingObjectQuarantineGraceHours,
            ["orphanFileQuarantineGraceHours"] = settings.OrphanFileQuarantineGraceHours,
            ["deleteGraceHours"] = settings.DeleteGraceHours,
            ["quarantineMissingObjects"] = settings.QuarantineMissingObjects,
            ["quarantineOrphanLocalFiles"] = settings.QuarantineOrphanLocalFiles,
            ["deleteQuarantinedObjects"] = settings.DeleteQuarantinedObjects
        };

        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Storage reconciliation is intentionally disabled.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            settings.DryRun
                ? "Storage reconciliation is enabled in dry-run mode."
                : "Storage reconciliation is enabled in policy-controlled mutation mode.",
            data));
    }
}
