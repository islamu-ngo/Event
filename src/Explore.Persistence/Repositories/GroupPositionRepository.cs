using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class GroupPositionRepository : GenericRepository<GroupPosition, int>, IGroupPositionRepository
{
    public GroupPositionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
