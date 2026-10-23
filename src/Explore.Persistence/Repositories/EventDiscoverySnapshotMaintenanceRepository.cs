using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventDiscoverySnapshotMaintenanceRepository(
    IGlobalExploreDbContextFactory globalFactory,
    ITenantExploreDbContextFactory tenantFactory)
    : IEventDiscoverySnapshotMaintenanceRepository
{
    public async Task<IReadOnlyList<EventDiscoverySnapshotReservation>> GetExpiredOwnersAsync(
        Guid? afterTenantId, DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        RequireBounds(nowUtc, take, PurgeEventDiscoverySnapshotsCommandHandler.TenantBatchSize);
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteReadCommittedAsync(
            token => EventDiscoverySnapshotProviderOperations.GetExpiredOwnersAsync(
                context, afterTenantId, nowUtc, take, token), cancellationToken);
    }

    public async Task<int> PurgeTenantBatchAsync(
        Guid tenantId, DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        RequireBounds(nowUtc, take, PurgeEventDiscoverySnapshotsCommandHandler.SnapshotBatchSize);
        await using var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<EventDiscoverySnapshotReservation>(
                context, tenantId, token);
            // Preserve reservation -> terminal epoch -> membership lock order, but
            // never create public authority: its Tenant FK may no longer exist.
            await RelationalNamedLock.AcquireTransactionAsync(
                context, $"discovery-epoch:{tenantId:N}", token);
            await context.Set<EventDiscoveryRevision>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(revision => revision.TenantId == tenantId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(revision => revision.DisclosureEpoch, revision => revision.DisclosureEpoch),
                    token);
            DateTime expiryCutoff = await EventDiscoverySnapshotProviderOperations.BoundExpiryAsync(
                context, nowUtc, token);
            var expired = context.Set<EventDiscoverySnapshot>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(snapshot => snapshot.TenantId == tenantId && snapshot.ExpiresAtUtc <= expiryCutoff);
            Guid[] ids = await expired.AsNoTracking().OrderBy(snapshot => snapshot.ExpiresAtUtc)
                .ThenBy(snapshot => snapshot.Id).Select(snapshot => snapshot.Id).Take(take)
                .ToArrayAsync(token);
            // SQL Server's migration policy removes database cascades. Delete the
            // bounded membership explicitly inside the same fenced transaction.
            await context.Set<EventDiscoverySnapshotItem>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(item => item.TenantId == tenantId && ids.Contains(item.SnapshotId))
                .ExecuteDeleteAsync(token);
            return await expired.Where(snapshot => ids.Contains(snapshot.Id)).ExecuteDeleteAsync(token);
        }, cancellationToken);
    }

    private static void RequireBounds(DateTime nowUtc, int take, int ceiling)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot maintenance time must be UTC.", nameof(nowUtc));
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, ceiling);
    }
}
