namespace Explore.Blazor.Client.Contracts.Services;

/// <summary>
/// Client-side translation service that fetches translations from the API,
/// caches them in memory, and provides quick key-based lookups.
/// </summary>
public interface ITranslationService
{
    /// <summary>Current language code (e.g., "en", "fr", "ar").</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Resolves a translation key to its value for the current language.
    /// Returns the key itself if no translation is found (safe fallback).
    /// </summary>
    string T(string key, string? fallback = null);

    /// <summary>
    /// Fetches all translations for the specified language code.
    /// Results are cached for 30 minutes.
    /// </summary>
    Task<IDictionary<string, string>> GetTranslationsAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Returns the list of available language codes from the API.
    /// Results are cached for 30 minutes.
    /// </summary>
    Task<ICollection<string>> GetAvailableLanguagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Changes the active language: clears cache, fetches new translations, and fires OnLanguageChanged.
    /// </summary>
    Task ChangeLanguageAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Preloads translations for the specified language without changing the active language.
    /// Called during initialization to ensure translations are ready before first render.
    /// </summary>
    Task PreloadAsync(string languageCode, CancellationToken ct = default);

    /// <summary>Fired when the active language changes. Argument is the new language code.</summary>
    event Action<string>? OnLanguageChanged;
}
