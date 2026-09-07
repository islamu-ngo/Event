using System.Collections.Immutable;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Translation Management System provider that abstracts translation import/export operations.
/// <para>
/// Implementations include TolgeeTranslationProvider, WeblateTranslationProvider,
/// OfflineTranslationProvider, and NullTranslationProvider.
/// Runtime switching is handled by RuntimeTranslationProvider wrapper via GovernanceSettings.
/// </para>
/// <para>
/// All implementations must be failure-safe — translation failures fall back to OfflineTranslationProvider.
/// </para>
/// </summary>
public interface ITranslationManagementProvider
{
    /// <summary>
    /// Tests the connection to the TMS provider.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Imports translation keys with their translations into the TMS.
    /// Used by admin to push lookup table content (master_code → full_name) to TMS for translators.
    /// </summary>
    Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default);

    /// <summary>
    /// Exports all translations for a specific language from the TMS.
    /// Returns flat key-value pairs matching the translation key convention.
    /// </summary>
    Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Gets available languages configured in the TMS project.
    /// </summary>
    Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default);
}

/// <summary>
/// A set of translation key-value pairs to import into the TMS.
/// Keys follow the convention: lookup.{entity_type}.{master_code}.{field}
/// </summary>
/// <param name="KeyName">Translation key (e.g., "lookup.tag.FIQH.full_name").</param>
/// <param name="Translations">Language code → translated value (e.g., {"en": "Fiqh", "fr": "Jurisprudence islamique"}).</param>
public sealed record TranslationKeyImport
{
    public TranslationKeyImport(string KeyName, IDictionary<string, string> Translations)
    {
        this.KeyName = KeyName;
        this.Translations = Translations.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public string KeyName { get; }
    public ImmutableDictionary<string, string> Translations { get; }
}

/// <summary>
/// A single exported translation from the TMS.
/// </summary>
/// <param name="KeyName">Translation key (e.g., "lookup.tag.FIQH.full_name").</param>
/// <param name="Value">Translated value in the requested language.</param>
public record TranslationExport(string KeyName, string Value);
