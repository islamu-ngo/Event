// for encrypted operational configuration with key versioning and concurrency support.

namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class AppSettingRepository : IAppSettingRepository
{
    private readonly ExploreDbContext _dbContext;

    public AppSettingRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppSetting?> GetByKeyAsync(string key)
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConfigKey == key);
    }

    public async Task<List<AppSetting>> GetByCategoryAsync(string? category = null)
    {
        var query = _dbContext.AppSettings.AsNoTracking();

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(s => s.Category == category);
        }

        return await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.ConfigKey)
            .ToListAsync();
    }

    public async Task<List<AppSetting>> GetSettingsNeedingReEncryptionAsync(int currentKeyVersion)
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .Where(s => s.KeyVersion < currentKeyVersion)
            .OrderBy(s => s.ConfigKey)
            .ToListAsync();
    }

    public async Task<List<AppSetting>> GetAllAsync()
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .OrderBy(s => s.ConfigKey)
            .ToListAsync();
    }

    public async Task<AppSetting> CreateAsync(AppSetting setting)
    {
        await _dbContext.AppSettings.AddAsync(setting);
        await _dbContext.SaveChangesAsync();
        return setting;
    }

    public async Task UpdateAsync(AppSetting setting)
    {
        // Attach and mark as modified for concurrency check
        _dbContext.AppSettings.Update(setting);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var setting = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.ConfigKey == key);
        if (setting == null)
        {
            return false;
        }

        _dbContext.AppSettings.Remove(setting);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .AnyAsync(s => s.ConfigKey == key);
    }

    public async Task BulkUpdateAsync(IEnumerable<AppSetting> settings)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            foreach (var setting in settings)
            {
                _dbContext.AppSettings.Update(setting);
            }
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
