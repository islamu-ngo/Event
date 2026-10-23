namespace Explore.Application.Contracts.Persistence;

/// <summary>Fixed fleet aggregates only; no tenant, payment, refund or provider identities.</summary>
public sealed record RefundReconciliationHealth(
    int Pending,
    int Unknown,
    int RequiresAction,
    int Failed,
    int CampaignsRequiringOperator,
    int DisputesDueSoon,
    int DisputesDueWithin72Hours,
    int DisputesOverdue,
    DateTime? OldestNonTerminalAt);

/// <summary>
/// Observes the refund fleet through independent Global authority. Missing or
/// invalid authority throws; it must never be represented as an empty fleet.
/// </summary>
public interface IRefundReconciliationHealthObserver
{
    Task<RefundReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt, CancellationToken cancellationToken);
}
