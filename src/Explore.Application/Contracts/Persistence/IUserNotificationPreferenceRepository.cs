namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IUserNotificationPreferenceRepository : IGenericRepository<UserNotificationPreference, Guid>
{
    Task<UserNotificationPreference?> GetByUserAndCategory(Guid tenantId, Guid userId, string category);

    Task<List<UserNotificationPreference>> GetAllForUser(Guid tenantId, Guid userId);
}
