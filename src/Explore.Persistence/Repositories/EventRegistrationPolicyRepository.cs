using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class EventRegistrationPolicyRepository : GenericRepository<EventRegistrationPolicy, int>, IEventRegistrationPolicyRepository
{
    public EventRegistrationPolicyRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
