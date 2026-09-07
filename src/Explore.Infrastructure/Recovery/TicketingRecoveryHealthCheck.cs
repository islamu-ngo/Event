using Explore.Application.Contracts.Recovery;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Recovery;

public sealed class TicketingRecoveryHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<TicketingRecoveryOperatorOptions> options,
    TimeProvider timeProvider) :
    IHealthCheck
{
    public const string Name = "ticketing-recovery";
    public static readonly string[] DataKeys =
    [
        "status",
        "pending_reissues",
        "ambiguous_effects",
        "dead_lettered_effects",
        "poison_effects",
        "oldest_due_age_seconds",
    ];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        TicketingRecoveryOperatorOptions configured = options.Value;
        if (!configured.Enabled)
        {
            var disabledData = new Dictionary<string, object>
            {
                ["status"] = "disabled",
                ["pending_reissues"] = 0,
                ["ambiguous_effects"] = 0,
                ["dead_lettered_effects"] = 0,
                ["poison_effects"] = 0,
                ["oldest_due_age_seconds"] = 0,
            };
            return HealthCheckResult.Healthy(
                "Ticketing recovery controls are disabled.",
                disabledData);
        }

        await using AsyncServiceScope scope =
            scopeFactory.CreateAsyncScope();
        ITicketingRecoveryOperatorStore store =
            scope.ServiceProvider.GetRequiredService<
                ITicketingRecoveryOperatorStore>();
        TicketingRecoveryAggregateHealth health =
            await store.GetAggregateHealthAsync(cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        double oldestDueAge = health.OldestDueAt.HasValue
            ? Math.Max(0, (now - health.OldestDueAt.Value).TotalSeconds)
            : 0;
        bool unhealthy =
            health.Failed > 0 ||
            oldestDueAge >= configured.UnhealthyOldestDueSeconds;
        bool degraded =
            health.RecoveryOnly > 0 ||
            health.AmbiguousEffects > 0 ||
            health.DeadLetteredEffects > 0 ||
            health.PoisonEffects > 0 ||
            health.PendingReissues >= configured.BacklogThreshold ||
            oldestDueAge >= configured.WarningOldestDueSeconds;
        string status = unhealthy
            ? "unhealthy"
            : degraded
                ? "degraded"
                : "healthy";
        var data = new Dictionary<string, object>
        {
            ["status"] = status,
            ["pending_reissues"] = health.PendingReissues,
            ["ambiguous_effects"] = health.AmbiguousEffects,
            ["dead_lettered_effects"] = health.DeadLetteredEffects,
            ["poison_effects"] = health.PoisonEffects,
            ["oldest_due_age_seconds"] = oldestDueAge,
        };
        return unhealthy
            ? HealthCheckResult.Unhealthy(
                "Ticketing recovery requires operator intervention.",
                data: data)
            : degraded
                ? HealthCheckResult.Degraded(
                    "Ticketing recovery has unresolved work.",
                    data: data)
                : HealthCheckResult.Healthy(
                    "Ticketing recovery is healthy.",
                    data);
    }
}
