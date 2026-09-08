using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantUserRoleGrantRepository : IGenericRepository<TenantUserRoleGrant, Guid>
{
    Task<TenantUserRoleGrant?> GetByTenantAndUser(Guid tenantId, Guid userId);
    Task<TenantUserRoleGrant?> GetByTenantUserAndRole(Guid tenantId, Guid tenantUserId, int roleId);
    Task<List<TenantUserRoleGrant>> GetByTenant(Guid tenantId);
    Task<List<TenantUserRoleGrant>> GetByUserId(Guid userId);
    Task<bool> HasActiveTenantUserRoleGrant(Guid tenantId, Guid userId);
    Task<bool> IsTenantAdmin(Guid tenantId, Guid userId);
    Task<bool> IsTenantAdminInCurrentTenantAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<TenantUserRoleGrant?> GetGrantWithDetails(Guid id);
    Task<List<TenantUserRoleGrant>> GetGrantsWithDetails();
}
