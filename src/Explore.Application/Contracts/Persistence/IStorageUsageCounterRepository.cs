using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IStorageUsageCounterRepository : IGenericRepository<StorageUsageCounter, Guid>
{
    Task<StorageUsageCounter?> GetByTenantAndProviderAsync(Guid tenantId, string provider, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<StorageUsageCounter> GetOrCreateAsync(Guid tenantId, string provider, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUsageCounter>> GetAllTrackedForInstanceStorageRecalculationAsync(CancellationToken cancellationToken);
}
