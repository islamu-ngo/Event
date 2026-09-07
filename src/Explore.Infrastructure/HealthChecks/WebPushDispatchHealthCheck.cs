using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.WebPush;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class WebPushDispatchHealthCheck(
    IOptions<WebPushSettings> options,
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = settings.Enabled,
            ["pollingIntervalSeconds"] = settings.PollingIntervalSeconds,
            ["batchSize"] = settings.BatchSize,
            ["maxAttemptCount"] = settings.MaxAttemptCount,
            ["processingLeaseTimeoutSeconds"] = settings.ProcessingLeaseTimeoutSeconds,
            ["dueDispatchWarningThreshold"] = settings.HealthDueDispatchWarningThreshold,
            ["staleProcessingWarningThreshold"] = settings.HealthStaleProcessingWarningThreshold,
            ["terminalFailureWarningThreshold"] = settings.HealthTerminalFailureWarningThreshold,
            ["consumerId"] = settings.ConsumerId
        };

        if (!settings.Enabled)
        {
            return HealthCheckResult.Degraded("Web Push dispatch is intentionally disabled.", data: data);
        }

        var now = DateTime.UtcNow;
        var processingStartedBefore = now.AddSeconds(-settings.ProcessingLeaseTimeoutSeconds);
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
        var dueDispatchCount = await repository.CountDueDispatchAsync(now, cancellationToken);
        var retryScheduledCount = await repository.CountRetryScheduledAsync(cancellationToken);
        var staleProcessingCount = await repository.CountStaleProcessingAsync(processingStartedBefore, cancellationToken);
        var terminalFailureCount = await repository.CountTerminalFailureAsync(cancellationToken);

        data["dueDispatchCount"] = dueDispatchCount;
        data["retryScheduledCount"] = retryScheduledCount;
        data["staleProcessingCount"] = staleProcessingCount;
        data["terminalFailureCount"] = terminalFailureCount;
        data["processingStartedBefore"] = processingStartedBefore;

        if (staleProcessingCount >= settings.HealthStaleProcessingWarningThreshold)
        {
            return HealthCheckResult.Degraded("Web Push dispatch has stale processing rows.", data: data);
        }

        if (terminalFailureCount >= settings.HealthTerminalFailureWarningThreshold)
        {
            return HealthCheckResult.Degraded("Web Push dispatch has terminal failure rows.", data: data);
        }

        if (dueDispatchCount >= settings.HealthDueDispatchWarningThreshold)
        {
            return HealthCheckResult.Degraded("Web Push dispatch due backlog is above the configured threshold.", data: data);
        }

        return HealthCheckResult.Healthy("Web Push dispatch is enabled.", data);
    }
}
