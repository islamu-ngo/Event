using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class EventContactShareExportRepository : GenericRepository<EventContactShareExport, Guid>, IEventContactShareExportRepository
{
    public EventContactShareExportRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
