using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Services;

public sealed class EventResourceAuditRetentionService(
    IEventResourceAuditOwnerDiscoveryRepository owners,
    IEventResourceAuditRetentionExecutionScopeFactory executionFactory,
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
            var tenants = await owners.GetTenantIdsWithAuditAsync(afterTenantId, TenantPageSize, cancellationToken);
            foreach (var tenant in tenants)
            {
                int removed;
                do
                {
                    await using var execution = await executionFactory.CreateAsync(tenant, cancellationToken);
                    removed = await execution.UnitOfWork.ExecuteSerializableAsync(async token =>
                    {
                        var policy = await execution.Governance.ReadAsync(tenant, token)
                            ?? throw new InvalidOperationException("Resource audit retention requires a valid current governance policy.");
                        DateTime? cutoff = policy.AuditRetentionDays == 0
                            ? null
                            : observedAtUtc.AddDays(-policy.AuditRetentionDays);
                        return await execution.Repository.DeleteExpiredBatchAsync(tenant, cutoff, DeleteBatchSize, token);
                    }, cancellationToken);
                    deleted += removed;
                } while (removed == DeleteBatchSize);
            }
            if (tenants.Count < TenantPageSize) return deleted;
            afterTenantId = tenants[^1];
        }
    }
}
