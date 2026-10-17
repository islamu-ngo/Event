using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventLocationPrivacyReviewQuery
{
    internal static async Task<int> CountAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await context.Database.SqlQueryRaw<int>(
                    $"SELECT {PostgresLocationPrivacyReviewContract.FunctionSql(context)}() AS \"Value\"")
                .SingleAsync(cancellationToken);
        return await context.EventLocations
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceLocationPrivacyReviewQueueDepth)
            .CountAsync(row => row.NeedsPrivacyReview, cancellationToken);
    }
}
