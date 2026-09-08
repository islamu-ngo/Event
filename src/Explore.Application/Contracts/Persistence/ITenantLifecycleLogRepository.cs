using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Repository for tenant lifecycle status transition audit logs.
/// </summary>
public interface ITenantLifecycleLogRepository : IGenericRepository<TenantLifecycleLog, Guid>
{
    Task<TenantLifecycleLog> CreateAsync(
        TenantLifecycleLog lifecycleLog,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all lifecycle transition logs for a specific tenant, ordered by most recent first.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="limit">Maximum number of entries to return (default 50).</param>
    Task<List<TenantLifecycleLog>> GetByTenantIdAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
