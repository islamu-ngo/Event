using Explore.Application.Contracts.Waitlist;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Infrastructure.Waitlist;

public sealed class FairReturnOrchestrationHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) :
    IHealthCheck
{
    public async Task<HealthCheckResult>
        CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using AsyncServiceScope scope =
            scopeFactory.CreateAsyncScope();
        IFairReturnOrchestrationRepository repository =
            scope.ServiceProvider.GetRequiredService<
                IFairReturnOrchestrationRepository>();
        DateTime now =
            timeProvider.GetUtcNow().UtcDateTime;
        FairReturnOrchestrationHealth health =
            await repository.GetHealthAsync(
                now,
                cancellationToken);
        var data = new Dictionary<
            string,
            object>
        {
            ["pending"] = health.Pending,
            ["processing"] = health.Processing,
            ["unknown"] = health.Unknown,
            ["dead_lettered"] =
                health.DeadLettered,
            ["oldest_pending_age_seconds"] =
                health.OldestPendingAt.HasValue
                    ? Math.Max(
                        0,
                        (now - health
                            .OldestPendingAt.Value)
                        .TotalSeconds)
                    : 0,
        };
        return health.DeadLettered > 0
            ? HealthCheckResult.Degraded(
                "Fair-return orchestration has " +
                "dead-lettered effects.",
                data: data)
            : HealthCheckResult.Healthy(
                "Fair-return orchestration is healthy.",
                data);
    }
}
