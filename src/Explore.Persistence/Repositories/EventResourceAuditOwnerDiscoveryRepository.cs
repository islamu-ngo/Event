using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceAuditOwnerDiscoveryRepository(IGlobalExploreDbContextFactory globalFactory)
    : IEventResourceAuditOwnerDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsWithAuditAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, EventResourceAuditRetentionService.TenantPageSize);
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteReadCommittedAsync(
            token => EventResourceAuditOwnerQuery.ReadAsync(context, afterTenantId, limit, token), cancellationToken);
    }
}
