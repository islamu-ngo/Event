// Provides query methods for audit trail retrieval, ordered by most recent first.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ConfigurationChangeLogRepository : GenericRepository<ConfigurationChangeLog, Guid>, IConfigurationChangeLogRepository
{
    private readonly ExploreDbContext _dbContext;

    public ConfigurationChangeLogRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ConfigurationChangeLog>> GetBySettingKey(string settingKey, int limit = 50)
    {
        return await _dbContext.Set<ConfigurationChangeLog>()
            .AsNoTracking()
            .Where(c => c.SettingKey == settingKey)
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<ConfigurationChangeLog>> GetByScope(ConfigurationScopeEnum scope, Guid? scopeId = null, int limit = 50)
    {
        var query = _dbContext.Set<ConfigurationChangeLog>()
            .AsNoTracking()
            .Where(c => c.SettingScopeId == (int)scope);

        if (scopeId.HasValue)
        {
            query = query.Where(c => c.ScopeId == scopeId.Value);
        }

        return await query
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<ConfigurationChangeLog>> GetByUserId(Guid userId, int limit = 50)
    {
        return await _dbContext.Set<ConfigurationChangeLog>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
