namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IUiThemePresetRepository : IGenericRepository<UiThemePreset, Guid>
{
    Task<IReadOnlyList<UiThemePreset>> GetAvailablePresetsForTenantAsync(Guid tenantId, bool activeOnly = true);
    Task<UiThemePreset?> GetByThemeKeyAsync(Guid? tenantId, string themeKey);
    Task<UiThemePreset?> GetDefaultPresetForTenantAsync(Guid? tenantId);
    Task<bool> ThemeKeyExistsAsync(Guid? tenantId, string themeKey, Guid? excludingPresetId = null);
}
