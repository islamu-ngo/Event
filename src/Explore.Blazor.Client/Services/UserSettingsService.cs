using System.Collections.Concurrent;
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
    private readonly IPublicExperienceService _publicExperienceService;

    private readonly object _jsModuleLock = new();
    private Task<IJSObjectReference>? _jsModuleTask;
    private Task? _disposeTask;
    private bool _disposed;
    private readonly ConcurrentDictionary<string,
        (SettingGroupResponseDto Response, DateTime ExpiresAt, long Generation)> _cache = new();
    private long _authenticationGeneration;
    private long _cacheGeneration;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public UserSettingsService(
        ISettingsClient apiClient,
        AuthenticationStateProvider authenticationStateProvider,
        IJSRuntime jsRuntime,
        ILogger<UserSettingsService> logger,
        IPublicExperienceService publicExperienceService)
    {
        _apiClient = apiClient;
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
        _logger = logger;
        _publicExperienceService = publicExperienceService;
        _authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _publicExperienceService.SettingsChanged += OnPublicExperienceSettingsChanged;
    }

    public async Task<SettingGroupResponseDto?> GetSettingsAsync(string category, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed))
            return null;

        try
        {
            long generation = Volatile.Read(ref _authenticationGeneration);
            long cacheGeneration = Volatile.Read(ref _cacheGeneration);
            bool authenticated = await IsAuthenticatedAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration)
                || cacheGeneration != Volatile.Read(ref _cacheGeneration))
                return null;

            if (authenticated && _cache.TryGetValue(category, out var cached)
                && cached.Generation == cacheGeneration && cached.ExpiresAt > DateTime.UtcNow)
                return cached.Response;

            if (authenticated)
            {
                var result = await GetAuthenticatedSettingsAsync(category, ct);
                if (result is null || generation != Volatile.Read(ref _authenticationGeneration)
                    || cacheGeneration != Volatile.Read(ref _cacheGeneration))
                {
                    return null;
                }

                _cache[category] = (result, DateTime.UtcNow.Add(CacheDuration), cacheGeneration);
                return result;
            }

            var keyPrefix = CategoryToKeyPrefix(category);
            var tenant = await GetAnonymousTenantAsync();
            if (tenant is null || generation != Volatile.Read(ref _authenticationGeneration)
                || cacheGeneration != Volatile.Read(ref _cacheGeneration))
                return null;

            var module = await GetJsModuleAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration)
                || cacheGeneration != Volatile.Read(ref _cacheGeneration))
                return null;

            var settings = await module.InvokeAsync<Dictionary<string, string>?>("getAll", ct, tenant, keyPrefix);

            if (settings is null || settings.Count == 0
                || generation != Volatile.Read(ref _authenticationGeneration)
                || cacheGeneration != Volatile.Read(ref _cacheGeneration))
                return null;

            var dto = new SettingGroupResponseDto
            {
                Category = category,
                Settings = settings.Select(kvp => new EffectiveSettingDto
                {
                    Key = kvp.Key,
                    Value = kvp.Value,
                    Source = SettingSource.UserPreference,
                    IsLocked = false,
                    CanEdit = true
                }).ToList()
            };

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
        if (Volatile.Read(ref _disposed))
            return null;

        if (values.Count == 0)
            return new BatchUpdateResponseDto { Success = true, Results = [] };

        try
        {
            InvalidateCache(category);
            long generation = Volatile.Read(ref _authenticationGeneration);
            bool authenticated = await IsAuthenticatedAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return null;

            if (authenticated)
            {
                var batchDto = new UpdateSettingBatchDto
                {
                    Values = new Dictionary<string, string>(values),
                    Mode = 0 // BestEffort — skip locked settings, apply the rest
                };
                var result = await _apiClient.UpdateUserSettingsBatchAsync(category, batchDto, cancellationToken: ct);
                return generation == Volatile.Read(ref _authenticationGeneration) ? result : null;
            }

            var prefix = CategoryToKeyPrefix(category) + ".";
            if (values.Keys.Any(key => !key.StartsWith(prefix, StringComparison.Ordinal)))
                return null;

            var tenant = await GetAnonymousTenantAsync();
            if (tenant is null || generation != Volatile.Read(ref _authenticationGeneration))
                return null;

            var module = await GetJsModuleAsync();
            var results = new List<SettingUpdateResultDto>();
            foreach (var (key, value) in values)
            {
                if (generation != Volatile.Read(ref _authenticationGeneration))
                    return null;

                bool applied = await module.InvokeAsync<bool>("set", ct, tenant, key, value);
                results.Add(new SettingUpdateResultDto { Key = key, Applied = applied });
            }

            if (generation != Volatile.Read(ref _authenticationGeneration))
                return null;

            return new BatchUpdateResponseDto
            {
                Success = results.All(result => result.Applied == true),
                Results = results
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to batch-update settings for category '{Category}'", category);
            return null;
        }
        finally
        {
            InvalidateCache(category);
        }
    }

    public async Task<bool> UpdateSettingAsync(string key, string value, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed))
            return false;

        try
        {
            InvalidateAllCaches();
            long generation = Volatile.Read(ref _authenticationGeneration);
            bool authenticated = await IsAuthenticatedAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            if (authenticated)
            {
                var dto = new UpdateSettingValueDto { Value = value };
                var result = await _apiClient.UpdateUserSettingAsync(key, dto, cancellationToken: ct);
                return result?.Success == true && generation == Volatile.Read(ref _authenticationGeneration);
            }

            if (!IsRegisteredAnonymousKey(key))
                return false;
            var tenant = await GetAnonymousTenantAsync();
            if (tenant is null || generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            var module = await GetJsModuleAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;
            bool applied = await module.InvokeAsync<bool>("set", ct, tenant, key, value);
            return applied && generation == Volatile.Read(ref _authenticationGeneration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update setting '{Key}'", key);
            return false;
        }
        finally
        {
            InvalidateAllCaches();
        }
    }

    public async Task<bool> ResetSettingAsync(string key, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed))
            return false;

        try
        {
            InvalidateAllCaches();
            long generation = Volatile.Read(ref _authenticationGeneration);
            bool authenticated = await IsAuthenticatedAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            if (authenticated)
            {
                var result = await _apiClient.ResetUserSettingAsync(key, cancellationToken: ct);
                return result?.Success == true && generation == Volatile.Read(ref _authenticationGeneration);
            }

            if (!IsRegisteredAnonymousKey(key))
                return false;
            var tenant = await GetAnonymousTenantAsync();
            if (tenant is null || generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            var module = await GetJsModuleAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;
            bool applied = await module.InvokeAsync<bool>("remove", ct, tenant, key);
            return applied && generation == Volatile.Read(ref _authenticationGeneration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset setting '{Key}'", key);
            return false;
        }
        finally
        {
            InvalidateAllCaches();
        }
    }

    public async Task<bool> ResetAllAsync(string category, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed))
            return false;

        try
        {
            InvalidateCache(category);
            long generation = Volatile.Read(ref _authenticationGeneration);
            bool authenticated = await IsAuthenticatedAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            if (authenticated)
            {
                // Recheck the context between resets so a change stops remaining writes.
                var current = await GetAuthenticatedSettingsAsync(category, ct);
                if (current is null || generation != Volatile.Read(ref _authenticationGeneration))
                    return false;
                foreach (var setting in current.Settings)
                {
                    if (generation != Volatile.Read(ref _authenticationGeneration))
                        return false;
                    var result = await _apiClient.ResetUserSettingAsync(setting.Key, cancellationToken: ct);
                    if (result?.Success != true)
                        return false;
                }

                return generation == Volatile.Read(ref _authenticationGeneration);
            }

            var keyPrefix = CategoryToKeyPrefix(category);
            var tenant = await GetAnonymousTenantAsync();
            if (tenant is null || generation != Volatile.Read(ref _authenticationGeneration))
                return false;

            var module = await GetJsModuleAsync();
            if (generation != Volatile.Read(ref _authenticationGeneration))
                return false;
            await module.InvokeAsync<int>("clearPrefix", ct, tenant, keyPrefix);
            return generation == Volatile.Read(ref _authenticationGeneration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset all settings for category '{Category}'", category);
            return false;
        }
        finally
        {
            InvalidateCache(category);
        }
    }

    public void InvalidateCache(string category)
    {
        Interlocked.Increment(ref _cacheGeneration);
        _cache.TryRemove(category, out _);
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> _)
    {
        OnPublicExperienceSettingsChanged();
    }

    private void OnPublicExperienceSettingsChanged()
    {
        Interlocked.Increment(ref _authenticationGeneration);
        InvalidateAllCaches();
    }

    private void InvalidateAllCaches()
    {
        Interlocked.Increment(ref _cacheGeneration);
        _cache.Clear();
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
        Task<IJSObjectReference> acquisition;
        lock (_jsModuleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            acquisition = _jsModuleTask ??= _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "/js/user-settings.js").AsTask();
        }

        try
        {
            return await acquisition;
        }
        catch
        {
            lock (_jsModuleLock)
            {
                if (!_disposed && ReferenceEquals(_jsModuleTask, acquisition))
                    _jsModuleTask = null;
            }
            throw;
        }
    }

    private async Task<string?> GetAnonymousTenantAsync()
    {
        var settings = await _publicExperienceService.GetCachedSettingsAsync();
        return settings?.TenantId is { } tenantId && tenantId != Guid.Empty
            ? tenantId.ToString("N")
            : null;
    }

    internal static string CategoryToKeyPrefix(string category) => category switch
    {
        "PublicExperiencePreferences" => "public_experience_preferences",
        "AiAssistantPreferences" => "ai_assistant_preferences",
        "UiShellPreferences" => "ui_shell_preferences",
        "AtprotoFederation" => "federation",
        "EventList" or "event-list" => "event_list",
        "HomeDiscovery" or "home-discovery" => "home_discovery",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unregistered anonymous preference category.")
    };

    private static bool IsRegisteredAnonymousKey(string key) =>
        key.StartsWith("public_experience_preferences.", StringComparison.Ordinal)
        || key.StartsWith("ai_assistant_preferences.", StringComparison.Ordinal)
        || key.StartsWith("ui_shell_preferences.", StringComparison.Ordinal)
        || key.StartsWith("federation.", StringComparison.Ordinal)
        || key.StartsWith("event_list.", StringComparison.Ordinal)
        || key.StartsWith("home_discovery.", StringComparison.Ordinal);

    public ValueTask DisposeAsync()
    {
        lock (_jsModuleLock)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposed, true);
                _authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
                _publicExperienceService.SettingsChanged -= OnPublicExperienceSettingsChanged;
                Interlocked.Increment(ref _authenticationGeneration);
                InvalidateAllCaches();
                _disposeTask = DisposeModuleAsync(_jsModuleTask);
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeModuleAsync(Task<IJSObjectReference>? acquisition)
    {
        if (acquisition is null)
            return;

        IJSObjectReference module;
        try
        {
            module = await acquisition;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "User settings module import failed before disposal");
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Expected when Blazor circuit disconnects before disposal
        }
    }
}
