using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class EventReportOperationsRepository(IGlobalExploreDbContextFactory factory) : IEventReportOperationsRepository
{
    public Task<int> CountExternalLinksBySyncStateAsync(EventReportSyncState state, CancellationToken cancellationToken) =>
        CountAsync(state, null, cancellationToken);

    public Task<int> CountExternalLinksBySyncStateBeforeAsync(EventReportSyncState state, DateTime before, CancellationToken cancellationToken) =>
        CountAsync(state, before, cancellationToken);

    private async Task<int> CountAsync(EventReportSyncState state, DateTime? before, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await EventReportOperationsQuery.TryCountNativeAsync(context, state, before, cancellationToken)
            ?? await new EventReportRepository(context).CountOperationalExternalLinksAsync(state, before, cancellationToken);
    }
}
