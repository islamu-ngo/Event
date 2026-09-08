using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IUserSettingsService
{
    /// <summary>
    /// Loads effective settings for a category, resolving from API (authenticated) or localStorage (anonymous).
    /// Returns null during SSR prerender or on failure.
    /// </summary>
    Task<SettingGroupResponseDto?> GetSettingsAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Batch-updates settings for a category using BestEffort mode.
    /// Authenticated: BFF-proxied API. Anonymous: localStorage.
    /// </summary>
    Task<BatchUpdateResponseDto?> UpdateSettingsBatchAsync(string category, IDictionary<string, string> values, CancellationToken ct = default);

    /// <summary>
    /// Updates a single setting by key.
    /// </summary>
    Task<bool> UpdateSettingAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Removes a user override for a single setting, reverting to the next scope in the cascade.
    /// </summary>
    Task<bool> ResetSettingAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all user overrides for a category, reverting everything to tenant/instance defaults.
    /// </summary>
    Task<bool> ResetAllAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Invalidates the in-memory cache for a category, forcing a fresh load on next access.
    /// </summary>
    void InvalidateCache(string category);
}
