using System.Globalization;

namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Unified appearance settings for background customization.
/// Used by events, organizations, groups, and users.
/// </summary>
public sealed class AppearanceSettings
{
    public string BackgroundColor { get; set; } = string.Empty;
    public string ImageUri { get; set; } = string.Empty;
    public string BackgroundEffect { get; set; } = "None";

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(BackgroundColor) &&
        string.IsNullOrWhiteSpace(ImageUri) &&
        (string.IsNullOrWhiteSpace(BackgroundEffect) ||
         BackgroundEffect.Equals("None", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Builds CSS inline styles for banner/hero sections from appearance settings.
/// Replaces three entity-specific helpers with one unified builder.
/// </summary>
public static class AppearanceStyleBuilder
{
    private const string LightText = "#FFFFFF";
    private const string DarkText = "#000000";

    /// <summary>
    /// Builds a CSS style string from appearance settings.
    /// </summary>
    /// <param name="settings">The appearance settings to apply.</param>
    /// <param name="fallbackColorHex">Fallback color when no background color is set.</param>
    /// <param name="additionalCss">Optional additional CSS properties prepended to the style (e.g., "aspect-ratio: 16/9; position: relative").</param>
    public static string BuildStyle(AppearanceSettings settings, string fallbackColorHex, string? additionalCss = null)
    {
        var color = string.IsNullOrWhiteSpace(settings.BackgroundColor)
            ? fallbackColorHex
            : settings.BackgroundColor.Trim();

        var mediaUrl = settings.ImageUri?.Trim();
        var overlay = ResolveOverlay(settings.BackgroundEffect);

        var prefix = string.IsNullOrWhiteSpace(additionalCss)
            ? string.Empty
            : additionalCss.TrimEnd().TrimEnd(';') + "; ";

        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return string.IsNullOrWhiteSpace(overlay)
                ? $"{prefix}background: {color};"
                : $"{prefix}background: {color}; background-image: {overlay};";
        }

        var mediaBackground = $"url('{mediaUrl}')";
        if (string.IsNullOrWhiteSpace(overlay))
        {
            return $"{prefix}background: {color}; background-image: {mediaBackground}; background-position: center; background-size: cover; background-repeat: no-repeat;";
        }

        return $"{prefix}background: {color}; background-image: {overlay}, {mediaBackground}; background-position: center; background-size: cover; background-repeat: no-repeat;";
    }

    /// <summary>
    /// Builds a content-surface style with appearance background plus WCAG-readable text custom properties.
    /// </summary>
    public static string BuildSurfaceStyle(AppearanceSettings settings, string fallbackColorHex, string? additionalCss = null)
    {
        var style = BuildStyle(settings, fallbackColorHex, additionalCss);
        var textColor = ResolveReadableTextColor(settings, fallbackColorHex);
        var mutedColor = textColor.Equals(LightText, StringComparison.OrdinalIgnoreCase)
            ? "rgba(255,255,255,0.84)"
            : "rgba(0,0,0,0.76)";

        return $"{style} --event-theme-text-color: {textColor}; --event-theme-muted-color: {mutedColor}; color: var(--event-theme-text-color);";
    }

    /// <summary>
    /// Builds a hero-style CSS string with 16:9 aspect ratio (for event detail/create pages).
    /// </summary>
    public static string BuildHeroStyle(AppearanceSettings settings, string fallbackColorHex)
        => BuildStyle(settings, fallbackColorHex, "aspect-ratio: 16/9; position: relative");

    /// <summary>
    /// Builds a banner-style CSS string without aspect ratio (for profile pages).
    /// </summary>
    public static string BuildBannerStyle(AppearanceSettings settings, string fallbackColorHex)
        => BuildStyle(settings, fallbackColorHex);

    private static string ResolveOverlay(string? effect)
    {
        return effect?.Trim() switch
        {
            "SoftOverlay" => "linear-gradient(rgba(0,0,0,0.24), rgba(0,0,0,0.24))",
            "StrongOverlay" => "linear-gradient(rgba(0,0,0,0.40), rgba(0,0,0,0.40))",
            "Blur" => "linear-gradient(rgba(0,0,0,0.18), rgba(0,0,0,0.18))",
            _ => string.Empty
        };
    }

    private static string ResolveReadableTextColor(AppearanceSettings settings, string fallbackColorHex)
    {
        var color = string.IsNullOrWhiteSpace(settings.BackgroundColor)
            ? fallbackColorHex
            : settings.BackgroundColor.Trim();

        if (!TryParseHexColor(color, out var background))
        {
            return DarkText;
        }

        var effectiveBackground = ApplyBlackOverlay(background, ResolveOverlayOpacity(settings.BackgroundEffect));
        var lightContrast = ContrastRatio(effectiveBackground, (255, 255, 255));
        var darkContrast = ContrastRatio(effectiveBackground, (0, 0, 0));

        return lightContrast >= darkContrast ? LightText : DarkText;
    }

    private static double ResolveOverlayOpacity(string? effect)
    {
        return effect?.Trim() switch
        {
            "SoftOverlay" => 0.24,
            "StrongOverlay" => 0.40,
            "Blur" => 0.18,
            _ => 0
        };
    }

    private static (int Red, int Green, int Blue) ApplyBlackOverlay((int Red, int Green, int Blue) color, double opacity)
    {
        if (opacity <= 0)
        {
            return color;
        }

        var remaining = 1 - opacity;
        return (
            ClampColorComponent(color.Red * remaining),
            ClampColorComponent(color.Green * remaining),
            ClampColorComponent(color.Blue * remaining));
    }

    private static int ClampColorComponent(double value)
        => Math.Clamp((int)Math.Round(value), 0, 255);

    private static double ContrastRatio((int Red, int Green, int Blue) first, (int Red, int Green, int Blue) second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((int Red, int Green, int Blue) color)
    {
        return 0.2126 * Linearize(color.Red) +
               0.7152 * Linearize(color.Green) +
               0.0722 * Linearize(color.Blue);
    }

    private static double Linearize(int component)
    {
        var value = component / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static bool TryParseHexColor(string color, out (int Red, int Green, int Blue) value)
    {
        value = default;
        var hex = color.Trim().TrimStart('#');

        if (hex.Length == 3)
        {
            hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
        }

        if (hex.Length != 6 ||
            !int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        value = (red, green, blue);
        return true;
    }
}
