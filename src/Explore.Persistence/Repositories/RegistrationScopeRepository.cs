using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class RegistrationScopeRepository : GenericRepository<RegistrationScope, int>, IRegistrationScopeRepository
{
    public RegistrationScopeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
