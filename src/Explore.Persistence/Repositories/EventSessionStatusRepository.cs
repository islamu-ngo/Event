using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class EventSessionStatusRepository : GenericRepository<EventSessionStatus, int>, IEventSessionStatusRepository
{
    public EventSessionStatusRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
