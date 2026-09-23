using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Services;

public sealed class EventResourceAuditRetentionService(
    IEventResourceAuditRetentionRepository repository,
    IEventResourceGovernancePolicyReader governance,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int TenantPageSize = 100;
    public const int DeleteBatchSize = 1000;

    public async Task<long> CleanupAsync(CancellationToken cancellationToken)
    {
        DateTime observedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        Guid? afterTenantId = null;
        long deleted = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tenants = await repository.GetTenantsWithAuditAsync(afterTenantId, TenantPageSize, cancellationToken);
            foreach (var tenant in tenants)
            {
                int removed;
                do
                {
                    removed = await unitOfWork.ExecuteSerializableAsync(async token =>
                    {
                        var policy = await governance.ReadAsync(tenant.Id, token)
                            ?? throw new InvalidOperationException("Resource audit retention requires a valid current governance policy.");
                        DateTime? cutoff = policy.AuditRetentionDays == 0
                            ? null
                            : observedAtUtc.AddDays(-policy.AuditRetentionDays);
                        return await repository.DeleteExpiredBatchAsync(tenant.Id, cutoff, DeleteBatchSize, token);
                    }, cancellationToken);
                    deleted += removed;
                } while (removed == DeleteBatchSize);
            }
            if (tenants.Count < TenantPageSize) return deleted;
            afterTenantId = tenants[^1].Id;
        }
    }
}
