using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public sealed class UserSettingsService : IUserSettingsService, IAsyncDisposable
{
    private readonly ISettingsClient _apiClient;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<UserSettingsService> _logger;

    private IJSObjectReference? _jsModule;
    private readonly Dictionary<string, (SettingGroupResponseDto Response, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public UserSettingsService(
        ISettingsClient apiClient,
        AuthenticationStateProvider authenticationStateProvider,
        IJSRuntime jsRuntime,
        ILogger<UserSettingsService> logger)
    {
        _apiClient = apiClient;
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<SettingGroupResponseDto?> GetSettingsAsync(string category, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(category, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Response;

        try
        {
            if (await IsAuthenticatedAsync())
            {
                var result = await GetAuthenticatedSettingsAsync(category, ct);
                if (result is null)
                {
                    return null;
                }

                _cache[category] = (result, DateTime.UtcNow.Add(CacheDuration));
                return result;
            }

            // Anonymous: load from localStorage
            if (!OperatingSystem.IsBrowser())
                return null; // SSR prerender — no JS interop available

            var module = await GetJsModuleAsync();
            var keyPrefix = CategoryToKeyPrefix(category);
            var settings = await module.InvokeAsync<Dictionary<string, string>?>("getAll", ct, keyPrefix);

            if (settings is null || settings.Count == 0)
                return null;

            var dto = new SettingGroupResponseDto
            {
                Category = category,
                Settings = settings.Select(kvp => new EffectiveSettingDto
                {
                    Key = kvp.Key,
                    Value = kvp.Value,
                    Source = (SettingSource)5, // User scope (local override)
                    IsLocked = false,
                    CanEdit = true
                }).ToList()
            };

            _cache[category] = (dto, DateTime.UtcNow.Add(CacheDuration));
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings for category '{Category}'", category);
            return null;
        }
    }

    public async Task<BatchUpdateResponseDto?> UpdateSettingsBatchAsync(
        string category, IDictionary<string, string> values, CancellationToken ct = default)
    {
        if (values.Count == 0)
            return new BatchUpdateResponseDto { Success = true, Results = [] };

        try
        {
            InvalidateCache(category);

            if (await IsAuthenticatedAsync())
            {
                var batchDto = new UpdateSettingBatchDto
                {
                    Values = new Dictionary<string, string>(values),
                    Mode = 0 // BestEffort — skip locked settings, apply the rest
                };
                return await _apiClient.UpdateUserSettingsBatchAsync(category, batchDto, cancellationToken: ct);
            }

            // Anonymous: persist each value to localStorage
            if (!OperatingSystem.IsBrowser())
                return null;

            var module = await GetJsModuleAsync();
            foreach (var (key, value) in values)
            {
                await module.InvokeVoidAsync("set", ct, key, value);
            }

            return new BatchUpdateResponseDto
            {
                Success = true,
                Results = values.Select(kvp => new SettingUpdateResultDto
                {
                    Key = kvp.Key,
                    Applied = true
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to batch-update settings for category '{Category}'", category);
            return null;
        }
    }

    public async Task<bool> UpdateSettingAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            _cache.Clear(); // Key could belong to any cached category

            if (await IsAuthenticatedAsync())
            {
                var dto = new UpdateSettingValueDto { Value = value };
                await _apiClient.UpdateUserSettingAsync(key, dto, cancellationToken: ct);
                return true;
            }

            if (!OperatingSystem.IsBrowser())
                return false;

            var module = await GetJsModuleAsync();
            await module.InvokeVoidAsync("set", ct, key, value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update setting '{Key}'", key);
            return false;
        }
    }

    public async Task<bool> ResetSettingAsync(string key, CancellationToken ct = default)
    {
        try
        {
            _cache.Clear();

            if (await IsAuthenticatedAsync())
            {
                await _apiClient.ResetUserSettingAsync(key, cancellationToken: ct);
                return true;
            }

            if (!OperatingSystem.IsBrowser())
                return false;

            var module = await GetJsModuleAsync();
            await module.InvokeVoidAsync("remove", ct, key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset setting '{Key}'", key);
            return false;
        }
    }

    public async Task<bool> ResetAllAsync(string category, CancellationToken ct = default)
    {
        try
        {
            InvalidateCache(category);

            if (await IsAuthenticatedAsync())
            {
                // Fetch current settings to identify all keys, then reset each in parallel
                var current = await GetAuthenticatedSettingsAsync(category, ct);
                if (current?.Settings is { Count: > 0 })
                {
                    var resetTasks = current.Settings
                        .Select(s => _apiClient.ResetUserSettingAsync(s.Key, cancellationToken: ct));
                    await Task.WhenAll(resetTasks);
                }

                return true;
            }

            // Anonymous: clear all localStorage entries for this category
            if (!OperatingSystem.IsBrowser())
                return false;

            var module = await GetJsModuleAsync();
            var keyPrefix = CategoryToKeyPrefix(category);
            await module.InvokeVoidAsync("clearPrefix", ct, keyPrefix);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset all settings for category '{Category}'", category);
            return false;
        }
    }

    public void InvalidateCache(string category)
    {
        _cache.Remove(category);
    }

    private async Task<SettingGroupResponseDto?> GetAuthenticatedSettingsAsync(string category, CancellationToken ct)
    {
        return await _apiClient.GetUserSettingsAsync(category, cancellationToken: ct);
    }

    private async Task<bool> IsAuthenticatedAsync()
    {
        AuthenticationState state =
            await _authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated == true;
    }

    private async ValueTask<IJSObjectReference> GetJsModuleAsync()
    {
        return _jsModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "/js/user-settings.js");
    }

    /// <summary>
    /// Converts API category (kebab-case "event-list") to key prefix (underscore "event_list").
    /// </summary>
    private static string CategoryToKeyPrefix(string category)
    {
        return category.Replace('-', '_');
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_jsModule is not null)
            {
                await _jsModule.DisposeAsync();
                _jsModule = null;
            }
        }
        catch (JSDisconnectedException)
        {
            // Expected when Blazor circuit disconnects before disposal
        }
    }
}
