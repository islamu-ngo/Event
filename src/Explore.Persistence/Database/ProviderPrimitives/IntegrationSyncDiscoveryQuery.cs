using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class IntegrationSyncDiscoveryQuery
{
    internal static async Task<IReadOnlyList<IntegrationSyncQueueCandidate>> ReadAsync(
        ExploreDbContext context,
        int batchSize,
        DateTime now,
        DateTime staleProcessingStartedBefore,
        CancellationToken cancellationToken)
    {
        return await context.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .IgnoreAutoIncludes().AsNoTracking()
            .Where(outbox => !outbox.IsDeleted && (
                ((outbox.Status == IntegrationSyncStatus.Pending || outbox.Status == IntegrationSyncStatus.RetryScheduled) &&
                 (outbox.NextAttemptAt == null || outbox.NextAttemptAt <= now)) ||
                (outbox.Status == IntegrationSyncStatus.Processing &&
                 (outbox.ProcessingStartedAt == null || outbox.ProcessingStartedAt <= staleProcessingStartedBefore))))
            .OrderBy(outbox => outbox.CreatedAt)
            .ThenBy(outbox => outbox.Id)
            .Take(batchSize)
            .Select(outbox => new IntegrationSyncQueueCandidate(outbox.TenantId, outbox.Id))
            .ToListAsync(cancellationToken);
    }
}
