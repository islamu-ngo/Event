namespace Explore.Application.DTOs.Appearance;

using System.Text.RegularExpressions;

public static partial class UiThemeInputRules
{
    [GeneratedRegex("^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex ThemeKeyRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("^rgba\\((25[0-5]|2[0-4]\\d|1?\\d?\\d),(25[0-5]|2[0-4]\\d|1?\\d?\\d),(25[0-5]|2[0-4]\\d|1?\\d?\\d),(0|0?\\.\\d+|1(\\.0+)?)\\)$", RegexOptions.Compiled)]
    private static partial Regex RgbaColorRegex();

    public static bool IsValidThemeKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ThemeKeyRegex().IsMatch(value.Trim().ToLowerInvariant());

    public static string NormalizeThemeKey(string value) => value.Trim().ToLowerInvariant();

    public static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex().IsMatch(value.Trim());

    public static bool IsHexOrRgbaColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (HexColorRegex().IsMatch(value.Trim()) || RgbaColorRegex().IsMatch(value.Trim()));

    public static string NormalizeHex(string value) => value.Trim().ToUpperInvariant();

    public static string NormalizeFlexibleColor(string value)
    {
        var trimmed = value.Trim();
        return IsHexColor(trimmed) ? NormalizeHex(trimmed) : trimmed.ToLowerInvariant();
    }
}
