using Explore.Application.Contracts.Recovery;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class TicketingRecoveryHealthObserver(
    IGlobalExploreDbContextFactory globalFactory) : ITicketingRecoveryHealthObserver
{
    public async Task<TicketingRecoveryAggregateHealth> GetAggregateHealthAsync(CancellationToken cancellationToken)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.BindTenantLease() is not null || context.IsTenantFilterBypassed ||
            context.CurrentUserService is not null)
            throw new InvalidOperationException("ticketing_recovery_health_requires_unbound_global_lease");
        return await TicketingRecoveryHealthQuery.ReadAsync(context, cancellationToken);
    }
}
