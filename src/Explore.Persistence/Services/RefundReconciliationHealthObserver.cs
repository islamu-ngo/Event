using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class RefundReconciliationHealthObserver(
    IGlobalExploreDbContextFactory globalFactory) : IRefundReconciliationHealthObserver
{
    public async Task<RefundReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt, CancellationToken cancellationToken)
    {
        if (observedAt == default || observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Refund health requires a non-default UTC observation.", nameof(observedAt));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.BindTenantLease() is not null || context.IsTenantFilterBypassed ||
            context.CurrentUserService is not null)
            throw new InvalidOperationException("refund_health_requires_unbound_global_lease");
        return await RefundReconciliationHealthQuery.ReadAsync(context, observedAt, cancellationToken);
    }
}
