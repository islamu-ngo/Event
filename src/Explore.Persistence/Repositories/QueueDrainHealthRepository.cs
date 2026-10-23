using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class QueueDrainHealthRepository(IGlobalExploreDbContextFactory globalFactory) : IQueueDrainHealthRepository
{
    public async Task<QueueDrainHealthSnapshot> GetSnapshotAsync(
        DateTime observedAt, DateTime integrationStaleBefore, CancellationToken cancellationToken)
    {
        await using var dbContext = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await QueueDrainHealthQuery.ReadAsync(dbContext, observedAt, integrationStaleBefore, cancellationToken);
    }
}
