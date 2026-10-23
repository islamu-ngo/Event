using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class PdsSyncQueueAdmission(
    IPdsSyncQueueAdmissionDbContextFactory admissionFactory,
    IPdsSettlementDbContextFactory settlementFactory) : IPdsSyncQueueAdmission
{
    public async Task<IReadOnlyList<PdsSyncClaim>> ClaimDueAsync(
        int batchSize,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return await new PdsSyncOutboxRepository(context, settlementFactory).ClaimDueAsync(
            batchSize, leaseOwner, claimedAt, leaseDuration, cancellationToken);
    }
}
