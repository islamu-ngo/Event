using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantRepository : IGenericRepository<Tenant, Guid>
{
    /// <summary>
    /// Reserves discovery retirement in the original human deletion owner, before human sources.
    /// Does not create or join a transaction.
    /// </summary>
    Task AcquireHumanDeletionReservationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Deletes within the already admitted original human owner; never opens another owner.</summary>
    Task<bool> DeleteHumanAsync(Guid tenantId, IPreparedHumanTenantMutation prepared,
        CancellationToken cancellationToken = default);
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
    /// <summary>Changes only tenant name/audit within the configuration import owner's current transaction.</summary>
    Task<bool> ApplyConfigurationImportIdentityAsync(
        Guid tenantId,
        string displayName,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken);
    Task<bool> TryTransitionStatusAsync(
        Guid id,
        int expectedStatusId,
        int newStatusId,
        DateTime updatedAt,
        Guid updatedBy,
        CancellationToken cancellationToken = default);
}
