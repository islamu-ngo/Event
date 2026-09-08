using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class NotificationEntityTypeRepository : GenericRepository<NotificationEntityType, int>, INotificationEntityTypeRepository
{
    public NotificationEntityTypeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
