using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IFooterLinkGroupRepository : IGenericRepository<TenantFooterLinkGroup, Guid>
{
    /// <summary>
    /// Gets all active link groups for a tenant with their links included, ordered by Order.
    /// When the tenant has no groups and instance groups are not locked, returns instance-default groups.
    /// </summary>
    Task<List<TenantFooterLinkGroup>> GetResolvedGroupsForTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets all groups owned by a specific tenant (admin list view).
    /// </summary>
    Task<List<TenantFooterLinkGroup>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets all instance-default groups (TenantId == null).
    /// </summary>
    Task<List<TenantFooterLinkGroup>> GetInstanceDefaultGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a group with all its links included.
    /// </summary>
    Task<TenantFooterLinkGroup?> GetWithLinksAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets the maximum Order value for a tenant's groups (or instance-default groups when tenantId is null).
    /// </summary>
    Task<int> GetMaxOrderAsync(Guid? tenantId, CancellationToken ct = default);
}
