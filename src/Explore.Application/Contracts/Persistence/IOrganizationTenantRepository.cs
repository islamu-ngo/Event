using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IOrganizationTenantRepository : IGenericRepository<OrganizationTenant, Guid>
{
    Task<OrganizationTenant?> GetByOrganizationAndTenant(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
