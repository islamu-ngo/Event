using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class EventLocationPrivacyReviewReader(IGlobalExploreDbContextFactory factory)
    : IEventLocationPrivacyReviewReader
{
    public async Task<int> CountNeedingPrivacyReviewAsync(CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await EventLocationPrivacyReviewQuery.CountAsync(context, cancellationToken);
    }
}
