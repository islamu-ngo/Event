using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantUserProfileRepository : GenericRepository<TenantUserProfile, Guid>, ITenantUserProfileRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantUserProfileRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantUserProfile?> GetByTenantUserAsync(Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantUserId == tenantUserId, cancellationToken);
    }
}
