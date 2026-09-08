using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class UserNotificationPreferenceRepository
    : GenericRepository<UserNotificationPreference, Guid>, IUserNotificationPreferenceRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserNotificationPreferenceRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserNotificationPreference?> GetByUserAndCategory(Guid tenantId, Guid userId, string category)
    {
        return await _dbContext.UserNotificationPreferences
            .FirstOrDefaultAsync(preference =>
                preference.TenantId == tenantId
                && preference.UserId == userId
                && preference.Category == category);
    }

    public async Task<List<UserNotificationPreference>> GetAllForUser(Guid tenantId, Guid userId)
    {
        return await _dbContext.UserNotificationPreferences
            .AsNoTracking()
            .Where(preference => preference.TenantId == tenantId && preference.UserId == userId)
            .OrderBy(preference => preference.Category)
            .ToListAsync();
    }
}
