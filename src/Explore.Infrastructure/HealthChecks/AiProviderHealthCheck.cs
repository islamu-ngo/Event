using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class AiProviderHealthCheck(
    IOptions<AiProviderSettings> options,
    AiProviderHealthReporter reporter,
    BusinessMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = reporter.Check(options.Value);
        var reason = health.Data.TryGetValue("reason", out var value) ? value?.ToString() : health.Status;

        metrics.RecordAiProviderHealthCheck(
            AiProviderDefaults.ProviderIdToLabel(options.Value.Provider),
            health.Healthy ? "healthy" : "unhealthy",
            reason);

        return Task.FromResult(health.Healthy
            ? HealthCheckResult.Healthy(health.Description, health.Data)
            : HealthCheckResult.Unhealthy(health.Description, data: health.Data));
    }
}
