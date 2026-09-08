// for group-specific setting overrides.

namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class GroupSettingRepository : GenericRepository<GroupSetting, Guid>, IGroupSettingRepository
{
    private readonly ExploreDbContext _dbContext;

    public GroupSettingRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GroupSetting?> GetByGroupAndKey(Guid groupId, string key)
    {
        return await _dbContext.GroupSettingOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GroupTenant.GroupId == groupId && s.SettingKey == key);
    }

    public async Task<List<GroupSetting>> GetAllForGroup(Guid groupId)
    {
        return await _dbContext.GroupSettingOverrides
            .AsNoTracking()
            .Where(s => s.GroupTenant.GroupId == groupId)
            .ToListAsync();
    }

    public async Task<bool> RemoveOverride(Guid groupId, string key)
    {
        var setting = await _dbContext.GroupSettingOverrides
            .FirstOrDefaultAsync(s => s.GroupTenant.GroupId == groupId && s.SettingKey == key);

        if (setting == null)
            return false;

        _dbContext.GroupSettingOverrides.Remove(setting);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}
