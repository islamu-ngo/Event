namespace Explore.Domain.ValueObjects;

public class UiThemePalette
{
    public required string Primary { get; set; }
    public required string PrimaryContrastText { get; set; }
    public required string Secondary { get; set; }
    public required string SecondaryContrastText { get; set; }
    public required string Background { get; set; }
    public required string Surface { get; set; }
    public required string AppbarBackground { get; set; }
    public required string AppbarText { get; set; }
    public required string DrawerBackground { get; set; }
    public required string DrawerText { get; set; }
    public required string DrawerIcon { get; set; }
    public required string TextPrimary { get; set; }
    public required string TextSecondary { get; set; }
    public required string Info { get; set; }
    public required string Success { get; set; }
    public required string Warning { get; set; }
    public required string Error { get; set; }
    public required string LinesDefault { get; set; }
    public required string Divider { get; set; }

    /// <summary>
    /// Normalizes a hex color string to uppercase #RRGGBB format.
    /// Accepts "#rgb", "#rrggbb", "rgb", "rrggbb" and produces "#RRGGBB".
    /// </summary>
    public static string NormalizeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#000000";
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length switch
        {
            3 => $"#{trimmed[0]}{trimmed[0]}{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}".ToUpperInvariant(),
            6 => $"#{trimmed}".ToUpperInvariant(),
            _ => $"#{trimmed}".ToUpperInvariant()
        };
    }

    private static string NormalizeFlexibleColor(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : NormalizeHex(value);
    }

    /// <summary>
    /// Returns a normalized palette with opaque colors in #RRGGBB format and supported rgba values preserved.
    /// </summary>
    public UiThemePalette Normalized() => new()
    {
        Primary = NormalizeHex(Primary),
        PrimaryContrastText = NormalizeHex(PrimaryContrastText),
        Secondary = NormalizeHex(Secondary),
        SecondaryContrastText = NormalizeHex(SecondaryContrastText),
        Background = NormalizeHex(Background),
        Surface = NormalizeHex(Surface),
        AppbarBackground = NormalizeFlexibleColor(AppbarBackground),
        AppbarText = NormalizeHex(AppbarText),
        DrawerBackground = NormalizeFlexibleColor(DrawerBackground),
        DrawerText = NormalizeHex(DrawerText),
        DrawerIcon = NormalizeHex(DrawerIcon),
        TextPrimary = NormalizeHex(TextPrimary),
        TextSecondary = NormalizeHex(TextSecondary),
        Info = NormalizeHex(Info),
        Success = NormalizeHex(Success),
        Warning = NormalizeHex(Warning),
        Error = NormalizeHex(Error),
        LinesDefault = NormalizeHex(LinesDefault),
        Divider = NormalizeFlexibleColor(Divider)
    };
}
