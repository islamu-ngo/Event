using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class GroupTenantRepository(ExploreDbContext dbContext)
    : GenericRepository<GroupTenant, Guid>(dbContext), IGroupTenantRepository
{
    public Task<GroupTenant?> GetByGroupAndTenant(
        Guid groupId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        dbContext.GroupTenants
            .Include(participation => participation.Group)
                .ThenInclude(group => group.Actor)
                    .ThenInclude(actor => actor!.Pii)
            .Include(participation => participation.ApprovalStatus)
            .FirstOrDefaultAsync(
                participation => participation.GroupId == groupId
                    && participation.TenantId == tenantId,
                cancellationToken);
}
