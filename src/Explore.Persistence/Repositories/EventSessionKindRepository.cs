using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class EventSessionKindRepository : GenericRepository<EventSessionKind, int>, IEventSessionKindRepository
{
    public EventSessionKindRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
