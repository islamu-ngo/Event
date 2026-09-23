using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventResourceAuditRetentionRepository
{
    Task<IReadOnlyList<Tenant>> GetTenantsWithAuditAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken);

    /// <summary>A null cutoff purges every audit row in the bounded tenant batch for retention zero.</summary>
    Task<int> DeleteExpiredBatchAsync(
        Guid tenantId, DateTime? inclusiveCutoffUtc, int limit, CancellationToken cancellationToken);
}
