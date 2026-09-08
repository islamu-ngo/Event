namespace Explore.Blazor.Services.Preferences;

using Explore.Blazor.Services;

public interface IBffPreferenceValidationService
{
    string ThemeModeValidationMessage { get; }

    string? NormalizeThemeMode(string? themeMode);

    string? NormalizeLanguage(string? languageCode);

    string? NormalizeDirection(string? direction);
}

public sealed class BffPreferenceValidationService : IBffPreferenceValidationService
{
    private static readonly string[] ValidThemeModes =
    [
        "system",
        "light",
        "dark",
        "lighthighcontrast",
        "darkhighcontrast",
        "custom"
    ];

    public string ThemeModeValidationMessage =>
        "Theme mode must be one of: system, light, dark, lighthighcontrast, darkhighcontrast, custom.";

    public string? NormalizeThemeMode(string? themeMode)
    {
        var normalized = themeMode?.Trim().ToLowerInvariant();
        return normalized is not null && ValidThemeModes.Contains(normalized)
            ? normalized
            : null;
    }

    public string? NormalizeLanguage(string? languageCode)
    {
        return BffCultureRegistry.TryNormalize(languageCode, out var normalized)
            ? normalized
            : null;
    }

    public string? NormalizeDirection(string? direction)
    {
        var normalized = direction?.Trim().ToLowerInvariant();
        return normalized is "auto" or "ltr" or "rtl"
            ? normalized
            : null;
    }
}
