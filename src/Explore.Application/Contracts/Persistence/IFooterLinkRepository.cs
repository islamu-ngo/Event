using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IFooterLinkRepository : IGenericRepository<TenantFooterLink, Guid>
{
    Task<TenantFooterLink?> GetByIdForTenantAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active links for a group, ordered by Order.
    /// </summary>
    Task<List<TenantFooterLink>> GetByGroupIdAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Gets the maximum Order value within a link group.
    /// </summary>
    Task<int> GetMaxOrderInGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all links belonging to a group. Used when deleting the parent group.
    /// </summary>
    Task DeleteByGroupIdAsync(Guid groupId, CancellationToken ct = default);
}
