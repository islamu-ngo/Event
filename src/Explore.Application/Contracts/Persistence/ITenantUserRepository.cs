using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantUserRepository : IGenericRepository<TenantUser, Guid>
{
    Task<TenantUser?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<TenantUser?> GetByTenantAndActorAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<List<TenantUser>> GetActiveTenantsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> TryRemoveMembershipAsync(
        Guid tenantId,
        Guid userId,
        Guid removedBy,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default);
}
