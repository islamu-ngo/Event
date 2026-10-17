using Explore.Application.Contracts.Persistence;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private object? _outboxExecutionClaim;

    internal GenericOutboxClaim? GenericOutboxExecutionClaim => _outboxExecutionClaim as GenericOutboxClaim;
    internal NotificationOutboxClaim? NotificationOutboxExecutionClaim => _outboxExecutionClaim as NotificationOutboxClaim;

    internal void BindOutboxExecutionClaim(GenericOutboxClaim claim) => BindOriginalOutboxClaim(claim);
    internal void BindOutboxExecutionClaim(NotificationOutboxClaim claim) => BindOriginalOutboxClaim(claim);

    private void BindOriginalOutboxClaim(object claim)
    {
        ValidateTenantLease();
        if (Interlocked.CompareExchange(ref _outboxExecutionClaim, claim, null) is not null)
            throw new InvalidOperationException("outbox_execution_already_bound");
    }
}
