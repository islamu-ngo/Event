using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RegistrationProviderSubmissionNativeClaim
{
    internal static async Task<IReadOnlyList<RegistrationProviderSubmissionWriteClaim>?> TryClaimAsync(
        ExploreDbContext dbContext, string leaseOwner, int batchSize, DateTime claimedAt,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql()) return null;
        string command = $$"""
            SELECT effect_id, tenant_id, registration_submission_id, registration_attempt_id,
                registration_provider_binding_id, lease_token, processing_fence, attempt_count, lease_expires_at
            FROM {{Explore.Persistence.Security.PostgresRegistrationProviderWriteQueueContract.FunctionSql(dbContext)}}({0}, {1}, {2}, {3})
            """;
        return await dbContext.Database.SqlQueryRaw<RegistrationProviderSubmissionWriteClaim>(
            command, leaseOwner.Trim(), batchSize, claimedAt, leaseDuration).ToListAsync(cancellationToken);
    }
}
