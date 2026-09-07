using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantRepository : IGenericRepository<Tenant, Guid>
{
    Task<Tenant?> GetTenantBySlug(string slug);
    Task<IReadOnlyList<Tenant>> GetBySlugsAsNoTrackingAsync(
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken = default);
    Task<int> GetActiveTenantCountAsync();
    Task<IReadOnlyList<Tenant>> GetActiveAsNoTrackingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetAllActiveForConfigurationManifestExportAsync(
        int maximumCount,
        CancellationToken cancellationToken);
    Task<Tenant?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> TryTransitionStatusAsync(
        Guid id,
        int expectedStatusId,
        int newStatusId,
        DateTime updatedAt,
        Guid updatedBy,
        CancellationToken cancellationToken = default);
}
