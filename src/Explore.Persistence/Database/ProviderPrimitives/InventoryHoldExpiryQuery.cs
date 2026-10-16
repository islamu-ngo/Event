using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class InventoryHoldExpiryQuery
{
    internal static async Task<IReadOnlyList<InventoryHoldExpiryTarget>> ReadAsync(
        ExploreDbContext context, DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await PostgresInventoryHoldExpiryDiscoveryContract.DiscoverAsync(
                context, utcNow, batchSize, cancellationToken);

        // Portable providers still use an independently selected Global connection.
        // Only identities leave this scope; Runtime reloads and locks all business facts.
        var holds = await context.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                && hold.ExpiresAt <= utcNow)
            .OrderBy(hold => hold.ExpiresAt).ThenBy(hold => hold.Id).Take(batchSize)
            .Select(hold => new InventoryHoldExpiryTarget(hold.TenantId, hold.RegistrationOrderId, hold.Id))
            .ToListAsync(cancellationToken);
        var recovery = await context.RegistrationOrders
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(order => order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.NeedsReconciliation)
            .OrderBy(order => order.UpdatedAt).ThenBy(order => order.Id).Take(batchSize)
            .Select(order => new InventoryHoldExpiryTarget(order.TenantId, order.Id, null))
            .ToListAsync(cancellationToken);
        return holds.Concat(recovery).ToArray();
    }
}
