using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class LocalWebhookDeliveryHealthCheck(
    IOptions<WebhookDeliveryProcessorSettings> settings,
    IOptionsMonitor<WebhookOptions> webhookOptions,
    IServiceScopeFactory scopeFactory,
    BusinessMetrics metrics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var processorSettings = settings.Value;
        var options = webhookOptions.CurrentValue;
        var localProviderSelected = !options.IsDisabled
            && (options.IsProvider(WebhookOptions.ProviderLocal)
                || options.IsProvider(WebhookOptions.ProviderComposite));
        var data = new Dictionary<string, object>
        {
            ["enabled"] = options.Enabled,
            ["provider"] = options.Provider,
            ["localProviderSelected"] = localProviderSelected,
            ["processorEnabled"] = processorSettings.Enabled,
            ["batchSize"] = processorSettings.BatchSize,
            ["processingLeaseTimeoutSeconds"] = processorSettings.ProcessingLeaseTimeoutSeconds,
            ["dueAttemptWarningThreshold"] = processorSettings.HealthDueAttemptWarningThreshold,
            ["staleSendingWarningThreshold"] = processorSettings.HealthStaleSendingWarningThreshold
        };

        if (!localProviderSelected)
        {
            return Report(
                HealthCheckResult.Healthy(
                    "Local webhook delivery is not the selected outgoing provider.",
                    data: data),
                WebhookTelemetryOutcome.NotSelected);
        }

        if (!processorSettings.Enabled)
        {
            return Report(
                HealthCheckResult.Degraded(
                    "Local webhook delivery processor is disabled.",
                    data: data),
                WebhookTelemetryOutcome.Disabled);
        }

        var now = DateTimeOffset.UtcNow;
        int dueLocalTargets;
        int staleDeliveringTargets;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
            dueLocalTargets = await targetRepository.CountDueAsync(now, cancellationToken);
            staleDeliveringTargets = await targetRepository.CountStaleDeliveringAsync(now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "Local webhook delivery readiness query failed.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        data["dueLocalTargets"] = dueLocalTargets;
        data["staleDeliveringTargets"] = staleDeliveringTargets;
        data["observedAtUtc"] = now;

        if (staleDeliveringTargets >= processorSettings.HealthStaleSendingWarningThreshold)
        {
            return Report(
                HealthCheckResult.Degraded(
                    "Local webhook delivery has stale target claims.",
                    data: data),
                WebhookTelemetryOutcome.Degraded);
        }

        if (dueLocalTargets >= processorSettings.HealthDueAttemptWarningThreshold)
        {
            return Report(
                HealthCheckResult.Degraded(
                    "Local webhook delivery due-target backlog is above the configured threshold.",
                    data: data),
                WebhookTelemetryOutcome.Degraded);
        }

        return Report(
            HealthCheckResult.Healthy(
                "Local webhook delivery queue is healthy.",
                data: data),
            WebhookTelemetryOutcome.Healthy);
    }

    private HealthCheckResult Report(
        HealthCheckResult result,
        WebhookTelemetryOutcome outcome)
    {
        metrics.RecordWebhookProviderHealthCheck(WebhookTelemetryProvider.Local, outcome);
        return result;
    }
}
