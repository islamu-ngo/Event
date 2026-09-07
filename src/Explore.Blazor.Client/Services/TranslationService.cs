using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Localization;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class TranslationService : ITranslationService, IDisposable
{
    private record CacheEntry<T>(T Data, DateTime ExpiresAt)
    {
        public bool IsValid => DateTime.UtcNow < ExpiresAt;
    }

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly ITranslationClient _apiClient;
    private readonly ILogger<TranslationService> _logger;
    private readonly SemaphoreSlim _translationLock = new(1, 1);
    private readonly SemaphoreSlim _languagesLock = new(1, 1);

    private CacheEntry<IDictionary<string, string>>? _translationsCache;
    private CacheEntry<ICollection<string>>? _languagesCache;
    private string _currentLanguage = "en";

    public string CurrentLanguage => _currentLanguage;

    public event Action<string>? OnLanguageChanged;

    public TranslationService(ITranslationClient apiClient, ILogger<TranslationService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // Hot path — MUST NOT touch I/O, emit metrics, open logger scopes, or start OTEL spans.
    // See blazor-localization-plan.md Enterprise Concerns → Performance.
    public string T(string key, string? fallback = null)
    {
        if (string.IsNullOrEmpty(key))
            return fallback ?? string.Empty;

        var cache = _translationsCache;
        if (cache is { IsValid: true } && cache.Data.TryGetValue(key, out var value))
            return value;

        return fallback ?? key;
    }

    public async Task<IDictionary<string, string>> GetTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        if (!CultureRegistry.TryGetEntry(languageCode, out var entry))
        {
            _logger.LogWarning(
                "[TRANSLATION] GetTranslationsAsync rejected unknown language '{Language}'; returning empty dictionary (cache not poisoned)",
                languageCode);
            return new Dictionary<string, string>();
        }

        var normalised = entry.Code;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Language"] = normalised,
            ["Operation"] = nameof(GetTranslationsAsync)
        });

        var cache = _translationsCache;
        if (cache is { IsValid: true } && string.Equals(_currentLanguage, normalised, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[TRANSLATION] Cache hit");
            return cache.Data;
        }

        await _translationLock.WaitAsync(ct);
        try
        {
            cache = _translationsCache;
            if (cache is { IsValid: true } && string.Equals(_currentLanguage, normalised, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[TRANSLATION] Cache hit (after lock)");
                return cache.Data;
            }

            _logger.LogInformation("[TRANSLATION] Cache miss, fetching from API");
            var translations = await _apiClient.GetTranslationByLanguageAsync(normalised, cancellationToken: ct);
            _translationsCache = new CacheEntry<IDictionary<string, string>>(translations, DateTime.UtcNow.Add(CacheDuration));
            return translations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRANSLATION] Failed to fetch translations");
            return _translationsCache?.Data ?? new Dictionary<string, string>();
        }
        finally
        {
            _translationLock.Release();
        }
    }

    public async Task<ICollection<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        var cache = _languagesCache;
        if (cache is { IsValid: true })
        {
            _logger.LogDebug("[TRANSLATION] Languages cache hit");
            return cache.Data;
        }

        await _languagesLock.WaitAsync(ct);
        try
        {
            cache = _languagesCache;
            if (cache is { IsValid: true })
                return cache.Data;

            _logger.LogDebug("[TRANSLATION] Languages cache miss, fetching from API");
            var languages = await _apiClient.GetAvailableTranslationLanguagesAsync(cancellationToken: ct);
            _languagesCache = new CacheEntry<ICollection<string>>(languages, DateTime.UtcNow.Add(CacheDuration));
            return languages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRANSLATION] Failed to fetch available languages");
            return _languagesCache?.Data ?? new List<string> { "en" };
        }
        finally
        {
            _languagesLock.Release();
        }
    }

    public async Task ChangeLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        if (!CultureRegistry.TryGetEntry(languageCode, out var entry))
        {
            _logger.LogWarning(
                "[TRANSLATION] ChangeLanguageAsync rejected unknown language '{Language}'; no-op",
                languageCode);
            return;
        }

        var normalised = entry.Code;
        if (string.Equals(_currentLanguage, normalised, StringComparison.OrdinalIgnoreCase))
            return;

        _logger.LogInformation("[TRANSLATION] Changing language from {Old} to {New}", _currentLanguage, normalised);

        _translationsCache = null;
        _currentLanguage = normalised;

        await GetTranslationsAsync(normalised, ct);
        OnLanguageChanged?.Invoke(normalised);
    }

    public async Task PreloadAsync(string languageCode, CancellationToken ct = default)
    {
        if (!CultureRegistry.TryGetEntry(languageCode, out var entry))
        {
            _logger.LogWarning(
                "[TRANSLATION] PreloadAsync rejected unknown language '{Language}'; preloading 'en' instead",
                languageCode);
            entry = CultureRegistry.GetAll()[0];
        }

        _currentLanguage = entry.Code;
        await GetTranslationsAsync(entry.Code, ct);
    }

    public void Dispose()
    {
        // Intentionally no-op. Blazor can dispose scoped services while first-render
        // async localization work is unwinding during navigation or circuit teardown;
        // disposing these SemaphoreSlim instances can turn a benign teardown race into
        // an ObjectDisposedException rendered by the global ErrorBoundary.
    }
}
