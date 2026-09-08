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
    private PublicExperienceSettingsDto? _cachedSettings;
    private DateTimeOffset _settingsCacheExpiresAt;
    private PublicExperienceShellDto? _cachedShell;
    private DateTimeOffset _shellCacheExpiresAt;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public event Action? SettingsChanged;

    public PublicExperienceAvailability SettingsAvailability { get; private set; }
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

    public async Task<PublicExperienceSettingsDto?> GetSettingsAsync()
    {
        try
        {
            var settings = await _apiClient.GetPublicExperienceSettingsAsync(cancellationToken: CancellationToken.None);
            if (!HasRequiredIdentity(settings))
            {
                InvalidateSettings();
                return settings;
            }

            CacheSettings(settings);
            return settings;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Failed to load public experience settings, falling back to event list. Status: {StatusCode}.",
                ex.StatusCode);
            InvalidateSettings();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load public experience settings, falling back to event list.");
            InvalidateSettings();
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
        if (_cachedSettings != null && _clock.GetUtcNow() <= _settingsCacheExpiresAt)
        {
            return _cachedSettings;
        }

        PublicExperienceSettingsDto? settings = await GetSettingsAsync();
        return SettingsAvailability == PublicExperienceAvailability.Available ? settings : null;
    }

    public async Task<PublicExperienceShellDto?> GetCachedShellAsync()
    {
        if (_cachedShell != null && _clock.GetUtcNow() <= _shellCacheExpiresAt)
        {
            return _cachedShell;
        }

        PublicExperienceShellDto? shell = await GetShellAsync();
        return ShellAvailability == PublicExperienceAvailability.Available ? shell : null;
    }

    public void ResetCache()
    {
        _cachedSettings = null;
        _settingsCacheExpiresAt = default;
        SettingsAvailability = PublicExperienceAvailability.Unknown;
        _cachedShell = null;
        _shellCacheExpiresAt = default;
        ShellAvailability = PublicExperienceAvailability.Unknown;
        SettingsChanged?.Invoke();
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

    private void CacheSettings(PublicExperienceSettingsDto? settings)
    {
        _cachedSettings = settings;
        _settingsCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        SettingsAvailability = PublicExperienceAvailability.Available;
    }

    private void CacheShell(PublicExperienceShellDto? shell)
    {
        _cachedShell = shell;
        _shellCacheExpiresAt = _clock.GetUtcNow().Add(CacheDuration);
        ShellAvailability = PublicExperienceAvailability.Available;
    }

    private void InvalidateSettings()
    {
        _cachedSettings = null;
        _settingsCacheExpiresAt = default;
        SettingsAvailability = PublicExperienceAvailability.Unavailable;
    }

    private void InvalidateShell()
    {
        _cachedShell = null;
        _shellCacheExpiresAt = default;
        ShellAvailability = PublicExperienceAvailability.Unavailable;
    }

    private static bool HasRequiredIdentity(PublicExperienceSettingsDto? settings) =>
        settings?.DirectoryOperator is not null;

    private static bool HasRequiredIdentity(PublicExperienceShellDto? shell) =>
        shell?.DirectoryOperator is not null && shell.InstanceOperator is not null;
}
