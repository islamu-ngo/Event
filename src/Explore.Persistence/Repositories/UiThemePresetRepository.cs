namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class UiThemePresetRepository : GenericRepository<UiThemePreset, Guid>, IUiThemePresetRepository
{
    private readonly ExploreDbContext _dbContext;

    public UiThemePresetRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UiThemePreset>> GetAvailablePresetsForTenantAsync(Guid tenantId, bool activeOnly = true)
    {
        var query = _dbContext.UiThemePresets
            .AsNoTracking()
            .Where(p => p.TenantId == null || p.TenantId == tenantId);

        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query
            .OrderBy(p => p.DisplayName)
            .ToListAsync();
    }

    public async Task<UiThemePreset?> GetByThemeKeyAsync(Guid? tenantId, string themeKey)
    {
        return await _dbContext.UiThemePresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ThemeKey == themeKey && !p.IsDeleted);
    }

    public async Task<UiThemePreset?> GetDefaultPresetForTenantAsync(Guid? tenantId)
    {
        return await _dbContext.UiThemePresets
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.DisplayName)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> ThemeKeyExistsAsync(Guid? tenantId, string themeKey, Guid? excludingPresetId = null)
    {
        return await _dbContext.UiThemePresets
            .AsNoTracking()
            .AnyAsync(p =>
                p.TenantId == tenantId
                && p.ThemeKey == themeKey
                && !p.IsDeleted
                && (!excludingPresetId.HasValue || p.Id != excludingPresetId.Value));
    }
}
