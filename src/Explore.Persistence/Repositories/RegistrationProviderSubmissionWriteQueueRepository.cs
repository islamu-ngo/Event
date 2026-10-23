using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubmissionWriteQueueRepository(
    IGlobalExploreDbContextFactory globalFactory) : IRegistrationProviderSubmissionWriteQueueRepository
{
    public async Task<IReadOnlyList<RegistrationProviderSubmissionWriteClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.BindTenantLease() is not null)
            throw new InvalidOperationException("Provider queue acquisition requires unbound Global authority.");
        return await new RegistrationProviderSubmissionWriteEffectRepository(context).ClaimDueAsync(
            leaseOwner, batchSize, claimedAt, leaseDuration, cancellationToken);
    }
}
