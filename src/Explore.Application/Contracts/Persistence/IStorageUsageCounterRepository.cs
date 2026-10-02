using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IStorageUsageCounterRepository : IGenericRepository<StorageUsageCounter, Guid>
{
    Task<StorageUsageCounter?> GetByTenantAndProviderAsync(Guid tenantId, string provider, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<StorageUsageCounter> GetOrCreateAsync(Guid tenantId, string provider, CancellationToken cancellationToken);
    /// <summary>
    /// Fences and rebuilds one authorized tenant/provider projection after custody
    /// mutations are flushed in the caller's transaction. Provider I/O is not permitted.
    /// </summary>
    Task<StorageUsageCounter> RecalculateScopeAsync(
        Guid tenantId, string provider, DateTime utcNow, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetAllTrackedForInstanceStorageRecalculationAsync(CancellationToken cancellationToken);
}
