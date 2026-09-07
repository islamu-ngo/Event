using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class OrganizationTenantRepository(ExploreDbContext dbContext)
    : GenericRepository<OrganizationTenant, Guid>(dbContext), IOrganizationTenantRepository
{
    public Task<OrganizationTenant?> GetByOrganizationAndTenant(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        dbContext.OrganizationTenants
            .Include(participation => participation.Organization)
                .ThenInclude(organization => organization.Pii)
            .Include(participation => participation.Organization)
                .ThenInclude(organization => organization.Actor)
                    .ThenInclude(actor => actor!.Pii)
            .Include(participation => participation.ApprovalStatus)
            .FirstOrDefaultAsync(
                participation => participation.OrganizationId == organizationId
                    && participation.TenantId == tenantId,
                cancellationToken);
}
