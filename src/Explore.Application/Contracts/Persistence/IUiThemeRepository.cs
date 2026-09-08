namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IUiThemeRepository : IGenericRepository<UiTheme, Guid>
{
    Task<UiTheme?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task ClearDefaultAsync(Guid? tenantId, Guid? excludingThemeId = null);
    Task<UiTheme?> GetByThemeKeyAsync(Guid? tenantId, string themeKey);
    Task<IReadOnlyList<UiTheme>> GetOwnedThemesAsync(Guid? tenantId, bool activeOnly = false);
    Task<IReadOnlyList<UiTheme>> GetAvailableThemesForTenantAsync(Guid tenantId, bool activeOnly = true);
    Task<UiTheme?> GetDefaultThemeAsync(Guid? tenantId);
    Task<bool> ThemeKeyExistsAsync(Guid? tenantId, string themeKey, Guid? excludingThemeId = null);
}
