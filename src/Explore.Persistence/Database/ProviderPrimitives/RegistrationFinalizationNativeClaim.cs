using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RegistrationFinalizationNativeClaim
{
    internal static async Task<IReadOnlyList<RegistrationFinalizationClaim>?> TryClaimAsync(
        ExploreDbContext context, string leaseOwner, int batchSize, DateTime claimedAt,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        string command = $$"""
            SELECT effect_id, tenant_id, registration_order_id, lease_token, processing_fence
            FROM {{PostgresRegistrationFinalizationQueueContract.FunctionSql(context)}}({0}, {1}, {2}, {3})
            """;
        return await context.Database.SqlQueryRaw<RegistrationFinalizationClaim>(
            command, leaseOwner.Trim(), batchSize, claimedAt, leaseDuration).ToListAsync(cancellationToken);
    }
}
