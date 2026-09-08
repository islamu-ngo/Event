namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Unified translation resolver that abstracts TMS provider selection and caching.
/// <para>
/// Resolution chain:
/// 1. Ask RuntimeTranslationProvider (live TMS or offline bundles)
/// 2. If empty → return key as fallback (English FullName for lookup tables)
/// 3. Results cached with HybridCache (30-min TTL for live, app-lifetime for offline)
/// </para>
/// <para>
/// Key convention:
/// - Lookup tables: <c>lookup.{entity_type}.{master_code}.{field}</c>
/// - UI strings: <c>ui.{area}.{component}.{element}</c>
/// </para>
/// </summary>
public interface ITranslationResolver
{
    /// <summary>
    /// Resolves a single translation key for the given language.
    /// Returns the translated value, or the key itself if no translation is found.
    /// </summary>
    /// <param name="key">Translation key (e.g., "lookup.tag.FIQH.full_name").</param>
    /// <param name="languageCode">ISO 639-1 language code (e.g., "fr", "ar").</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> ResolveAsync(string key, string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Resolves multiple translation keys for the given language in a single call.
    /// Returns a dictionary of key → translated value.
    /// Keys without translations return the key itself as the value.
    /// </summary>
    /// <param name="keys">Translation keys to resolve.</param>
    /// <param name="languageCode">ISO 639-1 language code.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IDictionary<string, string>> ResolveBatchAsync(IEnumerable<string> keys, string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached entries for the given language under the current tenant, across both
    /// <c>"live"</c> and <c>"offline"</c> provider-mode cache slots. Called by the admin "export to
    /// bundle" flow so a freshly-persisted bundle is picked up on the next read.
    /// </summary>
    Task InvalidateLanguageAsync(string languageCode, CancellationToken ct = default);
}
