using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class InstanceBootstrapStateRepository : GenericRepository<InstanceBootstrapState, Guid>, IInstanceBootstrapStateRepository
{
    private readonly ExploreDbContext _dbContext;

    public InstanceBootstrapStateRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default)
    {
        return await _dbContext.InstanceBootstrapStates
            .AsNoTracking()
            .OrderByDescending(x => x.Generation)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<InstanceBootstrapState?> GetCurrentForUpdate(
        CancellationToken cancellationToken = default) =>
        RelationalInstanceBootstrapStateLock.LoadCurrentAsync(
            _dbContext,
            cancellationToken);
}
