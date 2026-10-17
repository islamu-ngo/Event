using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class IntegrationSyncQueueDiscovery(IGlobalExploreDbContextFactory globalFactory)
    : IIntegrationSyncQueueDiscovery
{
    public async Task<IReadOnlyList<IntegrationSyncQueueCandidate>> GetPendingAsync(
        int batchSize, DateTime now, DateTime staleProcessingStartedBefore, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, IIntegrationSyncQueueDiscovery.MaximumBatchSize);
        if (now.Kind != DateTimeKind.Utc || staleProcessingStartedBefore.Kind != DateTimeKind.Utc ||
            staleProcessingStartedBefore > now)
            throw new ArgumentException("Integration sync discovery requires ordered UTC bounds.");

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await IntegrationSyncDiscoveryQuery.ReadAsync(
            context, batchSize, now, staleProcessingStartedBefore, cancellationToken);
    }
}
