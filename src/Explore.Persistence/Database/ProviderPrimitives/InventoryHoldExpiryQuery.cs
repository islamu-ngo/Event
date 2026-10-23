using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class InventoryHoldExpiryQuery
{
    internal static async Task<IReadOnlyList<InventoryHoldExpiryTarget>> ReadAsync(
        ExploreDbContext context, DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        // Independent limits share one statement; an order may appear in both branches.
        // Runtime reloads and locks all settlement facts.
        var holds = context.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).IgnoreAutoIncludes().AsNoTracking()
            .Where(hold => !hold.IsDeleted
                && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                && hold.ExpiresAt <= utcNow)
            .OrderBy(hold => hold.ExpiresAt).ThenBy(hold => hold.Id).Take(batchSize)
            .Select(hold => new
            {
                hold.TenantId,
                OrderId = hold.RegistrationOrderId,
                CandidateId = hold.Id,
                Bucket = 0,
                SortAt = (DateTime?)hold.ExpiresAt,
                Cursor = hold.Id
            });
        var recovery = context.RegistrationOrders
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).IgnoreAutoIncludes().AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.NeedsReconciliation)
            .OrderBy(order => order.UpdatedAt).ThenBy(order => order.Id).Take(batchSize)
            .Select(order => new
            {
                order.TenantId,
                OrderId = order.Id,
                CandidateId = order.Id,
                Bucket = 1,
                SortAt = order.UpdatedAt,
                Cursor = order.Id
            });
        return await holds.Concat(recovery)
            .OrderBy(target => target.Bucket).ThenBy(target => target.SortAt).ThenBy(target => target.Cursor)
            .Select(target => new InventoryHoldExpiryTarget(
                target.TenantId, target.OrderId, target.Bucket == 0 ? target.CandidateId : null))
            .ToArrayAsync(cancellationToken);
    }
}
