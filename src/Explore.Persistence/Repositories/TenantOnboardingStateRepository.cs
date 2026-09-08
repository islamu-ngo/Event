using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantOnboardingStateRepository : GenericRepository<TenantOnboardingState, Guid>, ITenantOnboardingStateRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantOnboardingStateRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantOnboardingState?> GetByTenantId(Guid tenantId)
    {
        return await _dbContext.TenantOnboardingStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId);
    }
}
