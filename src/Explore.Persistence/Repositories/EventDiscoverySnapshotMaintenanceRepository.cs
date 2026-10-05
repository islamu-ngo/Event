using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class EventDiscoverySnapshotMaintenanceRepository(ExploreDbContext context)
    : IEventDiscoverySnapshotMaintenanceRepository
{
    public async Task<IReadOnlyList<EventDiscoverySnapshotReservation>> GetExpiredOwnersAsync(
        Guid? afterTenantId, DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        RequireBounds(nowUtc, take, PurgeEventDiscoverySnapshotsCommandHandler.TenantBatchSize);
        return await EventDiscoverySnapshotProviderOperations.GetExpiredOwnersAsync(
            context, afterTenantId, nowUtc, take, cancellationToken);
    }

    public async Task<int> PurgeTenantBatchAsync(
        Guid tenantId, DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        RequireBounds(nowUtc, take, PurgeEventDiscoverySnapshotsCommandHandler.SnapshotBatchSize);
        if (tenantId == Guid.Empty || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Snapshot maintenance requires an exact tenant and Serializable.");
        return await EventDiscoveryDisclosureRepository.InTenantAsync(context, tenantId, async () =>
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<EventDiscoverySnapshotReservation>(
                context, tenantId, cancellationToken);
            // Preserve reservation -> terminal epoch -> membership lock order, but
            // never create public authority: its Tenant FK may no longer exist.
            await RelationalNamedLock.AcquireTransactionAsync(
                context, $"discovery-epoch:{tenantId:N}", cancellationToken);
            await context.Set<EventDiscoveryRevision>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(revision => revision.TenantId == tenantId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(revision => revision.DisclosureEpoch, revision => revision.DisclosureEpoch),
                    cancellationToken);
            DateTime expiryCutoff = await EventDiscoverySnapshotProviderOperations.BoundExpiryAsync(
                context, nowUtc, cancellationToken);
            var expired = context.Set<EventDiscoverySnapshot>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(snapshot => snapshot.TenantId == tenantId && snapshot.ExpiresAtUtc <= expiryCutoff);
            Guid[] ids = await expired.AsNoTracking().OrderBy(snapshot => snapshot.ExpiresAtUtc)
                .ThenBy(snapshot => snapshot.Id).Select(snapshot => snapshot.Id).Take(take)
                .ToArrayAsync(cancellationToken);
            // SQL Server's migration policy removes database cascades. Delete the
            // bounded membership explicitly inside the same fenced transaction.
            await context.Set<EventDiscoverySnapshotItem>()
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .Where(item => item.TenantId == tenantId && ids.Contains(item.SnapshotId))
                .ExecuteDeleteAsync(cancellationToken);
            return await expired.Where(snapshot => ids.Contains(snapshot.Id)).ExecuteDeleteAsync(cancellationToken);
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
