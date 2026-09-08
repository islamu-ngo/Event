using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ILocationRepository : IGenericRepository<Location, Guid>
{
    Task<Location> Create(Location location, CancellationToken cancellationToken);
    Task<Location?> GetById(Guid id, CancellationToken cancellationToken);
    Task Update(Location location, CancellationToken cancellationToken);
    Task<List<Location>> GetLocationsByTenant(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetExistingTenantLocationIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> candidateLocationIds,
        CancellationToken cancellationToken = default);
    Task<List<Location>> GetOwnedPrivateHomesForGlobalErasureAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
    Task<(List<Location> Items, int TotalCount)> GetLocationsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes PII data for a location (GDPR erasure).
    /// Uses ExecuteDeleteAsync for efficient bulk deletion without loading entities.
    /// </summary>
    /// <returns>Number of PII records deleted.</returns>
    Task<int> ForgetPiiAsync(Guid locationId, CancellationToken cancellationToken = default);
}
