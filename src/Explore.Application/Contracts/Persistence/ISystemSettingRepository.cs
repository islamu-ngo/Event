// for system-wide configuration settings with optional locking.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

/// <summary>
/// Repository for system-wide settings.
/// </summary>
public interface ISystemSettingRepository
{
    /// <summary>
    /// Gets a setting by its unique key.
    /// </summary>
    Task<SystemSetting?> GetByKey(string key, CancellationToken cancellationToken = default);

    Task<string?> UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    Task<string?> UpsertInCurrentTransactionAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default);

    Task<string?> UpsertLockAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all settings, optionally filtered by category.
    /// </summary>
    Task<List<SystemSetting>> GetAllSettings(
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a setting exists and is locked.
    /// </summary>
    Task<bool> IsLocked(string key, CancellationToken cancellationToken = default);
}
