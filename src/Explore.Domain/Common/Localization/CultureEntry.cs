namespace Explore.Domain.Common.Localization;

/// <summary>
/// Immutable description of a culture supported by the codebase.
/// </summary>
/// <param name="Code">ISO 639-1 lowercase language code (e.g. "en", "fr", "ar").</param>
/// <param name="DisplayName">English display name (e.g. "English", "French", "Arabic").</param>
/// <param name="NativeName">Name in the language itself (e.g. "English", "Français", "العربية").</param>
/// <param name="Flag">Emoji flag representing the language.</param>
/// <param name="IsRtl">Whether the language is written right-to-left.</param>
public sealed record CultureEntry(
    string Code,
    string DisplayName,
    string NativeName,
    string Flag,
    bool IsRtl);
