namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class UiThemeRepository : GenericRepository<UiTheme, Guid>, IUiThemeRepository
{
    private readonly ExploreDbContext _dbContext;

    public UiThemeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<UiTheme?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.UiThemes
            .AsNoTracking()
            .FirstOrDefaultAsync(theme => theme.Id == id, cancellationToken);

    public async Task ClearDefaultAsync(Guid? tenantId, Guid? excludingThemeId = null)
    {
        var defaults = await _dbContext.UiThemes
            .Where(theme => theme.TenantId == tenantId && theme.IsDefault)
            .Where(theme => !excludingThemeId.HasValue || theme.Id != excludingThemeId.Value)
            .ToListAsync();

        if (defaults.Count == 0)
        {
            return;
        }

        foreach (var theme in defaults)
        {
            theme.IsDefault = false;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<UiTheme?> GetByThemeKeyAsync(Guid? tenantId, string themeKey)
    {
        return await _dbContext.UiThemes
            .AsNoTracking()
            .FirstOrDefaultAsync(theme => theme.TenantId == tenantId && theme.ThemeKey == themeKey);
    }

    public async Task<IReadOnlyList<UiTheme>> GetOwnedThemesAsync(Guid? tenantId, bool activeOnly = false)
    {
        var query = _dbContext.UiThemes
            .AsNoTracking()
            .Where(theme => theme.TenantId == tenantId);

        if (activeOnly)
        {
            query = query.Where(theme => theme.IsActive);
        }

        return await query
            .OrderByDescending(theme => theme.IsDefault)
            .ThenBy(theme => theme.SortOrder)
            .ThenBy(theme => theme.DisplayName)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UiTheme>> GetAvailableThemesForTenantAsync(Guid tenantId, bool activeOnly = true)
    {
        var query = _dbContext.UiThemes
            .AsNoTracking()
            .Where(theme => theme.TenantId == null || theme.TenantId == tenantId);

        if (activeOnly)
        {
            query = query.Where(theme => theme.IsActive);
        }

        return await query
            .OrderByDescending(theme => theme.TenantId == tenantId)
            .ThenByDescending(theme => theme.IsDefault)
            .ThenBy(theme => theme.SortOrder)
            .ThenBy(theme => theme.DisplayName)
            .ToListAsync();
    }

    public async Task<UiTheme?> GetDefaultThemeAsync(Guid? tenantId)
    {
        return await _dbContext.UiThemes
            .AsNoTracking()
            .Where(theme => theme.TenantId == tenantId && theme.IsDefault)
            .OrderBy(theme => theme.SortOrder)
            .ThenBy(theme => theme.DisplayName)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> ThemeKeyExistsAsync(Guid? tenantId, string themeKey, Guid? excludingThemeId = null)
    {
        return await _dbContext.UiThemes
            .AsNoTracking()
            .AnyAsync(theme =>
                theme.TenantId == tenantId
                && theme.ThemeKey == themeKey
                && (!excludingThemeId.HasValue || theme.Id != excludingThemeId.Value));
    }
}
