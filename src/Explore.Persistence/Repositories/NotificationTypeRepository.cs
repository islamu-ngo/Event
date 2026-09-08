using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public class NotificationTypeRepository : GenericRepository<NotificationType, int>, INotificationTypeRepository
{
    public NotificationTypeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
    }
}
