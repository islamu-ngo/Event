using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
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
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT tenant_id, outbox_id
                FROM {{PostgresIntegrationSyncDiscoveryContract.FunctionSql(context)}}({0}, {1}, {2})
                """;
            return await context.Database.SqlQueryRaw<IntegrationSyncQueueCandidate>(
                command, now, staleProcessingStartedBefore, batchSize).ToListAsync(cancellationToken);
        }

        return await context.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(outbox =>
                ((outbox.Status == IntegrationSyncStatus.Pending || outbox.Status == IntegrationSyncStatus.RetryScheduled) &&
                 (outbox.NextAttemptAt == null || outbox.NextAttemptAt <= now)) ||
                (outbox.Status == IntegrationSyncStatus.Processing &&
                 (outbox.ProcessingStartedAt == null || outbox.ProcessingStartedAt <= staleProcessingStartedBefore)))
            .OrderBy(outbox => outbox.CreatedAt)
            .ThenBy(outbox => outbox.Id)
            .Take(batchSize)
            .Select(outbox => new IntegrationSyncQueueCandidate(outbox.TenantId, outbox.Id))
            .ToListAsync(cancellationToken);
    }
}
