using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public enum PublicExperienceAvailability
{
    Unknown,
    Available,
    Unavailable
}

public interface IPublicExperienceService
{
    event Action? SettingsChanged;

    PublicExperienceAvailability SettingsAvailability { get; }
    PublicExperienceAvailability ShellAvailability { get; }

    Task<PublicExperienceSettingsDto?> GetSettingsAsync();
    Task<PublicExperienceShellDto?> GetShellAsync();
    string ResolveHomeRoute(PublicExperienceSettingsDto? settings);
    string ResolveHomeRoute(PublicExperienceShellDto? shell);
    Task<PublicExperienceSettingsDto?> GetCachedSettingsAsync();
    Task<PublicExperienceShellDto?> GetCachedShellAsync();
    void ResetCache();
}

public class PublicExperienceService : IPublicExperienceService
{
    private readonly IPublicExperienceClient _apiClient;
    private readonly ILogger<PublicExperienceService> _logger;
    private readonly TimeProvider _clock;
    private readonly object _settingsGate = new();
    private long _settingsGeneration;
    private PublicExperienceAvailability _settingsAvailability;
    private PublicExperienceSettingsDto? _cachedSettings;
    private ISLAMU.Wire.Contracts.Announcements.AnnouncementPublicStateDto? _announcementSnapshot;
    private DateTimeOffset _settingsCacheExpiresAt;
    private PublicExperienceShellDto? _cachedShell;
    private DateTimeOffset _shellCacheExpiresAt;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public event Action? SettingsChanged;

    public PublicExperienceAvailability SettingsAvailability
    {
        get
        {
            lock (_settingsGate)
                return _settingsAvailability;
        }
    }
    public PublicExperienceAvailability ShellAvailability { get; private set; }

    public PublicExperienceService(
        IPublicExperienceClient apiClient,
        ILogger<PublicExperienceService> logger,
        TimeProvider? clock = null)
    {
        _apiClient = apiClient;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<PublicExperienceSettingsDto?> GetSettingsAsync()
    {
        long generation;
        lock (_settingsGate)
            generation = ++_settingsGeneration;

        return GetSettingsAsync(generation);
    }

    private async Task<PublicExperienceSettingsDto?> GetSettingsAsync(long generation)
    {
        try
        {
            var settings = await _apiClient.GetPublicExperienceSettingsAsync(cancellationToken: CancellationToken.None);
            lock (_settingsGate)
            {
                if (generation != _settingsGeneration)
                    return null;

                if (!HasRequiredIdentity(settings))
                    InvalidateSettings();
                else
                    CacheSettings(settings);

                // A synchronous notification may itself start a refresh or reset.
                return generation == _settingsGeneration ? settings : null;
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Failed to load public experience settings, falling back to event list. Status: {StatusCode}.",
                ex.StatusCode);
            lock (_settingsGate)
            {
                if (generation == _settingsGeneration)
                    InvalidateSettings();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load public experience settings, falling back to event list.");
            lock (_settingsGate)
            {
                if (generation == _settingsGeneration)
                    InvalidateSettings();
            }
            return null;
        }
    }

    public async Task<PublicExperienceShellDto?> GetShellAsync()
    {
        try
        {
            var shell = await _apiClient.GetPublicExperienceShellAsync(cancellationToken: CancellationToken.None);
            if (!HasRequiredIdentity(shell))
            {
                InvalidateShell();
                return shell;
            }

            CacheShell(shell);
            return shell;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Failed to load public experience shell, falling back to event list. Status: {StatusCode}.",
                ex.StatusCode);
            InvalidateShell();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load public experience shell, falling back to event list.");
            InvalidateShell();
            return null;
        }
    }

    public async Task<PublicExperienceSettingsDto?> GetCachedSettingsAsync()
    {
        long generation;
        lock (_settingsGate)
        {
            if (_cachedSettings != null && _clock.GetUtcNow() <= _settingsCacheExpiresAt)
                return _cachedSettings;
            if (_settingsAvailability == PublicExperienceAvailability.Unavailable
                && _clock.GetUtcNow() <= _settingsCacheExpiresAt)
                return null;

            generation = ++_settingsGeneration;
        }

        PublicExperienceSettingsDto? settings = await GetSettingsAsync(generation);
        lock (_settingsGate)
            return generation == _settingsGeneration
                && _settingsAvailability == PublicExperienceAvailability.Available ? settings : null;
    }

    public async Task<PublicExperienceShellDto?> GetCachedShellAsync()
    {
        if (ShellAvailability == PublicExperienceAvailability.Unavailable
            && _clock.GetUtcNow() <= _shellCacheExpiresAt)
            return null;
        if (_cachedShell != null && _clock.GetUtcNow() <= _shellCacheExpiresAt)
        {
            return _cachedShell;
        }

        PublicExperienceShellDto? shell = await GetShellAsync();
        return ShellAvailability == PublicExperienceAvailability.Available ? shell : null;
    }

    public void ResetCache()
    {
        lock (_settingsGate)
        {
            ++_settingsGeneration;
            _cachedSettings = null;
            _announcementSnapshot = null;
            _settingsCacheExpiresAt = default;
            _settingsAvailability = PublicExperienceAvailability.Unknown;
            _cachedShell = null;
            _shellCacheExpiresAt = default;
            ShellAvailability = PublicExperienceAvailability.Unknown;
            SettingsChanged?.Invoke();
        }
    }

    public string ResolveHomeRoute(PublicExperienceSettingsDto? settings)
    {
        return settings?.PreferredHomePage?.Equals("LandingPage", StringComparison.OrdinalIgnoreCase) == true
            ? "/home"
            : "/events";
    }

    public string ResolveHomeRoute(PublicExperienceShellDto? shell)
    {
        if (shell?.Home?.PreferredHomePage?.Equals("LandingPage", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "/home";
        }

        if (shell?.Mode == PublicExperienceMode.OrganizationCentric
            && shell.PrimaryOrganization?.State == PublicExperiencePrimaryOrganizationState.Available)
        {
            return "/home";
        }

        return "/events";
    }

    private void CacheSettings(PublicExperienceSettingsDto settings)
    {
        var announcement = AnnouncementClientService.SnapshotPublic(settings.Announcement);
        bool changed = _settingsAvailability != PublicExperienceAvailability.Available
            || _cachedSettings is not { } previous
            || previous.TenantId != settings.TenantId
            || _announcementSnapshot != announcement;
        _cachedSettings = settings;
        _announcementSnapshot = announcement;
        _settingsCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        _settingsAvailability = PublicExperienceAvailability.Available;
        if (changed)
            SettingsChanged?.Invoke();
    }

    private void CacheShell(PublicExperienceShellDto? shell)
    {
        _cachedShell = shell;
        _shellCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        ShellAvailability = PublicExperienceAvailability.Available;
    }

    private void InvalidateSettings()
    {
        bool changed = _settingsAvailability != PublicExperienceAvailability.Unavailable;
        _cachedSettings = null;
        _announcementSnapshot = null;
        _settingsCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        _settingsAvailability = PublicExperienceAvailability.Unavailable;
        if (changed)
            SettingsChanged?.Invoke();
    }

    private void InvalidateShell()
    {
        _cachedShell = null;
        _shellCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        ShellAvailability = PublicExperienceAvailability.Unavailable;
    }

    private static bool HasRequiredIdentity(PublicExperienceSettingsDto? settings) =>
        settings?.DirectoryOperator is not null;

    private static bool HasRequiredIdentity(PublicExperienceShellDto? shell) =>
        shell?.DirectoryOperator is not null && shell.InstanceOperator is not null;
}
