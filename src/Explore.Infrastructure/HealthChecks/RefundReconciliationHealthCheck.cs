using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Infrastructure.HealthChecks;

public sealed class RefundReconciliationHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IHealthCheck
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;
            RefundReconciliationHealth health = await scope.ServiceProvider
                .GetRequiredService<IRefundAttemptRepository>()
                .GetReconciliationHealthAsync(observedAt, cancellationToken);
            bool stale = health.OldestNonTerminalAt.HasValue && observedAt - health.OldestNonTerminalAt.Value >= StaleAfter;
            var data = new Dictionary<string, object>
            {
                ["pending"] = health.Pending,
                ["unknown"] = health.Unknown,
                ["requiresAction"] = health.RequiresAction,
                ["failed"] = health.Failed,
                ["campaignsRequiringOperator"] = health.CampaignsRequiringOperator,
                ["disputesDueSoon"] = health.DisputesDueSoon,
                ["disputesDueWithin72Hours"] = health.DisputesDueWithin72Hours,
                ["disputesOverdue"] = health.DisputesOverdue,
                ["oldestNonTerminalAtUtc"] = health.OldestNonTerminalAt?.ToString("O") ?? string.Empty
            };
            return stale || health.Unknown > 0 || health.RequiresAction > 0 ||
                   health.Failed > 0 || health.CampaignsRequiringOperator > 0 ||
                   health.DisputesDueSoon > 0 || health.DisputesDueWithin72Hours > 0 || health.DisputesOverdue > 0
                ? HealthCheckResult.Degraded("Refund reconciliation requires operator attention.", data: data)
                : HealthCheckResult.Healthy("Refund reconciliation is healthy.", data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Refund reconciliation readiness query failed.");
        }
    }
}
