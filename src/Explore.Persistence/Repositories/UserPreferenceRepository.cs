// for user-specific setting overrides.

namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class UserPreferenceRepository : GenericRepository<UserPreference, Guid>, IUserPreferenceRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserPreferenceRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserPreference?> GetByUserAndKey(Guid tenantId, Guid userId, string key)
    {
        return await _dbContext.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == userId && s.SettingKey == key);
    }

    public async Task<List<UserPreference>> GetAllForUser(Guid tenantId, Guid userId)
    {
        return await _dbContext.UserPreferences
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.UserId == userId)
            .ToListAsync();
    }

    public async Task<bool> RemoveOverride(Guid tenantId, Guid userId, string key)
    {
        var setting = await _dbContext.UserPreferences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == userId && s.SettingKey == key);

        if (setting == null)
            return false;

        _dbContext.UserPreferences.Remove(setting);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}
