using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IGroupTenantRepository : IGenericRepository<GroupTenant, Guid>
{
    Task<GroupTenant?> GetByGroupAndTenant(
        Guid groupId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
