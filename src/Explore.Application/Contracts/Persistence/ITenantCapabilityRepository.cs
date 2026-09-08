// data access for tenant module capabilities and governance.

using Explore.Domain.Modules;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Repository for tenant capabilities (module enablement).
/// </summary>
public interface ITenantCapabilityRepository : IGenericRepository<TenantCapability, Guid>
{
    /// <summary>
    /// Gets all capabilities for a specific tenant.
    /// </summary>
    Task<List<TenantCapability>> GetByTenantId(Guid tenantId);

    /// <summary>
    /// Gets all enabled capabilities for a specific tenant with module details.
    /// </summary>
    Task<List<TenantCapability>> GetEnabledByTenantId(Guid tenantId);

    /// <summary>
    /// Checks if a specific module is enabled for a tenant.
    /// </summary>
    Task<bool> IsModuleEnabled(Guid tenantId, string moduleKey);

    /// <summary>
    /// Gets a capability by tenant and module key.
    /// </summary>
    Task<TenantCapability?> GetByTenantAndModuleKey(
        Guid tenantId,
        string moduleKey,
        CancellationToken cancellationToken = default);

    Task<TenantCapability> Create(TenantCapability entity, CancellationToken cancellationToken = default);

    Task Update(TenantCapability entity, CancellationToken cancellationToken = default);
}
