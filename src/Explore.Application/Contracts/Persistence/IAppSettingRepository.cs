// for encrypted operational configuration with key versioning support.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

/// <summary>
/// Repository for encrypted operational configuration settings.
/// </summary>
public interface IAppSettingRepository
{
    /// <summary>
    /// Gets a setting by its unique key.
    /// </summary>
    /// <param name="key">The configuration key (e.g., "Smtp:Host").</param>
    /// <returns>The setting if found, null otherwise.</returns>
    Task<AppSetting?> GetByKeyAsync(string key);

    /// <summary>
    /// Gets all settings, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category filter.</param>
    /// <returns>List of settings matching the filter.</returns>
    Task<List<AppSetting>> GetByCategoryAsync(string? category = null);

    /// <summary>
    /// Gets all settings that need re-encryption (key version less than current).
    /// </summary>
    /// <param name="currentKeyVersion">The current encryption key version.</param>
    /// <returns>List of settings needing re-encryption.</returns>
    Task<List<AppSetting>> GetSettingsNeedingReEncryptionAsync(int currentKeyVersion);

    /// <summary>
    /// Gets all settings for loading into IConfiguration.
    /// </summary>
    /// <returns>All settings in the database.</returns>
    Task<List<AppSetting>> GetAllAsync();

    /// <summary>
    /// Creates a new setting.
    /// </summary>
    /// <param name="setting">The setting to create.</param>
    /// <returns>The created setting.</returns>
    Task<AppSetting> CreateAsync(AppSetting setting);

    /// <summary>
    /// Updates an existing setting.
    /// Throws DbUpdateConcurrencyException if RowVersion doesn't match.
    /// </summary>
    /// <param name="setting">The setting to update.</param>
    Task UpdateAsync(AppSetting setting);

    /// <summary>
    /// Deletes a setting by key.
    /// </summary>
    /// <param name="key">The configuration key to delete.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// Checks if a setting exists.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>True if exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Bulk updates settings (for key rotation).
    /// Uses transactions internally.
    /// </summary>
    /// <param name="settings">The settings to update.</param>
    Task BulkUpdateAsync(IEnumerable<AppSetting> settings);
}
