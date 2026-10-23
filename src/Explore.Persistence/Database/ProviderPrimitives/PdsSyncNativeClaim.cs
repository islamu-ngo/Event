using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class PdsSyncNativeClaim
{
    internal static Task<IReadOnlyList<PdsSyncClaim>>? ClaimAsync(
        ExploreDbContext context, int batchSize, string normalizedOwner, DateTime claimedAt,
        TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        context.Database.IsNpgsql()
            ? PostgresPdsQueueAdmissionContract.ClaimAsync(
                context, batchSize, normalizedOwner, claimedAt, leaseDuration, cancellationToken)
            : null;
}
