namespace Explore.Application.Contracts.Recovery;

/// <summary>Fixed fleet counts and time only; no recovery rows or mutation authority.</summary>
public sealed record TicketingRecoveryAggregateHealth(
    int RecoveryOnly,
    int Failed,
    int PendingReissues,
    int AmbiguousEffects,
    int DeadLetteredEffects,
    int PoisonEffects,
    DateTime? OldestDueAt);

/// <summary>Observes recovery readiness through separately acquired Global authority.</summary>
public interface ITicketingRecoveryHealthObserver
{
    Task<TicketingRecoveryAggregateHealth> GetAggregateHealthAsync(CancellationToken cancellationToken);
}
