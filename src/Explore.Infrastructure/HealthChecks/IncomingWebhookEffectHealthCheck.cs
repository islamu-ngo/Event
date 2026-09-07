using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class IncomingWebhookEffectHealthCheck(
    IOptions<IncomingWebhookProcessingSettings> settings,
    IServiceScopeFactory scopeFactory,
    BusinessMetrics metrics,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = settings.Value;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = options.Enabled,
            ["batchSize"] = options.BatchSize,
            ["maxConcurrentItems"] = options.MaxConcurrentItems,
            ["leaseSeconds"] = options.LeaseSeconds,
            ["backlogWarningThreshold"] = options.EffectBacklogWarningThreshold,
            ["staleLeaseWarningThreshold"] = options.EffectStaleLeaseWarningThreshold
        };
        if (!options.Enabled)
        {
            return Report(
                HealthCheckResult.Degraded("Incoming Coop effect processing is disabled.", data: data),
                WebhookTelemetryOutcome.Disabled);
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        int due;
        int stale;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncomingWebhookEffectOutboxRepository>();
            due = await repository.CountDueAsync(observedAt, cancellationToken);
            stale = await repository.CountStaleAsync(observedAt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Report(
                HealthCheckResult.Unhealthy("Incoming Coop effect readiness query failed.", data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        data["dueEffects"] = due;
        data["staleLeases"] = stale;
        data["observedAtUtc"] = observedAt;
        if (stale >= options.EffectStaleLeaseWarningThreshold)
        {
            return Report(
                HealthCheckResult.Degraded("Incoming Coop effect processing has stale claims.", data: data),
                WebhookTelemetryOutcome.Degraded);
        }

        if (due >= options.EffectBacklogWarningThreshold)
        {
            return Report(
                HealthCheckResult.Degraded("Incoming Coop effect backlog exceeds its warning threshold.", data: data),
                WebhookTelemetryOutcome.Degraded);
        }

        return Report(
            HealthCheckResult.Healthy("Incoming Coop effect processing is healthy.", data: data),
            WebhookTelemetryOutcome.Healthy);
    }

    private HealthCheckResult Report(HealthCheckResult result, WebhookTelemetryOutcome outcome)
    {
        metrics.RecordWebhookProviderHealthCheck(WebhookTelemetryProvider.Coop, outcome);
        return result;
    }
}
