using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantUserProfileRepository : IGenericRepository<TenantUserProfile, Guid>
{
    Task<TenantUserProfile?> GetByTenantUserAsync(Guid tenantUserId, CancellationToken cancellationToken = default);
}
