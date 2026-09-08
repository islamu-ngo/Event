using Explore.Blazor.Client.Localization;

namespace Explore.Blazor.Client.Models;

/// <summary>
/// Represents the current language context for the application.
/// Provided as a CascadingValue by LanguageProvider.
/// <para>
/// All display metadata (flag, display name, RTL flag) is sourced from
/// <see cref="CultureRegistry"/> — this type is a thin view-model over the shared registry
/// plus the user's direction override.
/// </para>
/// </summary>
public class LanguageContext
{
    /// <summary>ISO 639-1 language code (e.g., "en", "fr", "ar").</summary>
    public string LanguageCode { get; set; } = "en";

    /// <summary>Whether the current language is right-to-left (based on language code alone).</summary>
    public bool IsRtl { get; set; }

    /// <summary>
    /// User direction override: "auto" (use language default), "ltr", or "rtl".
    /// When "auto", <see cref="EffectiveIsRtl"/> follows <see cref="IsRtl"/>.
    /// </summary>
    public string DirectionOverride { get; set; } = "auto";

    /// <summary>
    /// Resolved direction considering both language and user override.
    /// "rtl" forces RTL, "ltr" forces LTR, "auto" defers to language-based <see cref="IsRtl"/>.
    /// </summary>
    public bool EffectiveIsRtl => DirectionOverride switch
    {
        "rtl" => true,
        "ltr" => false,
        _ => IsRtl
    };

    /// <summary>Display name of the current language (e.g., "English", "Français", "العربية").</summary>
    public string LanguageName { get; set; } = "English";

    /// <summary>Emoji flag representing the language.</summary>
    public string Flag { get; set; } = "🇺🇸";

    /// <summary>
    /// Creates a <see cref="LanguageContext"/> from a culture code by looking it up in <see cref="CultureRegistry"/>.
    /// Unknown codes fall back to the first registry entry (canonical default).
    /// </summary>
    public static LanguageContext ForLanguage(string? languageCode)
    {
        if (CultureRegistry.TryGetEntry(languageCode, out var entry))
        {
            return new LanguageContext
            {
                LanguageCode = entry.Code,
                IsRtl = entry.IsRtl,
                LanguageName = entry.NativeName,
                Flag = entry.Flag
            };
        }

        var fallback = CultureRegistry.GetAll()[0];
        return new LanguageContext
        {
            LanguageCode = fallback.Code,
            IsRtl = fallback.IsRtl,
            LanguageName = fallback.NativeName,
            Flag = fallback.Flag
        };
    }
}
