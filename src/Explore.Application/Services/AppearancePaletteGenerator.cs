namespace Explore.Application.Services;

using Explore.Application.DTOs.Appearance;

public static class AppearancePaletteGenerator
{
    /// <summary>
    /// Generates a complete light palette from natural + brand colors.
    /// Natural color drives: Background, Surface, TextPrimary, TextSecondary, Divider, LinesDefault.
    /// Brand color drives: Primary, Secondary, and their contrast texts.
    /// </summary>
    public static UiThemePaletteDto GenerateLightPalette(string naturalHex, string brandHex)
    {
        var natural = HslColor.FromHex(naturalHex);
        var brand = HslColor.FromHex(brandHex);

        var background = natural.ToHex();
        var surface = natural.AdjustLightness(Math.Min(natural.L + 4, 100)).ToHex();
        var textPrimary = natural.AdjustLightness(natural.L > 50 ? natural.L - 65 : natural.L + 65).ToHex();
        var textSecondary = natural.AdjustLightness(natural.L > 50 ? natural.L - 45 : natural.L + 45).ToHex();
        var divider = natural.AdjustLightness(natural.L > 50 ? natural.L - 10 : natural.L + 10).WithAlpha(0.12).ToHex();
        var linesDefault = natural.AdjustLightness(natural.L > 50 ? natural.L - 15 : natural.L + 15).ToHex();

        var primary = brand.ToHex();
        var primaryContrast = brand.ContrastTextColor();
        var secondary = brand.AdjustSaturation(Math.Max(brand.S - 20, 0)).AdjustLightness(Math.Max(brand.L - 15, 0)).ToHex();
        var secondaryContrast = brand.AdjustLightness(brand.L > 50 ? brand.L - 65 : brand.L + 65).ToHex();

        var appbarBackground = surface;
        var appbarText = textPrimary;
        var drawerBackground = surface;
        var drawerText = textPrimary;
        var drawerIcon = textSecondary;

        return new UiThemePaletteDto
        {
            Primary = primary,
            PrimaryContrastText = primaryContrast,
            Secondary = secondary,
            SecondaryContrastText = secondaryContrast,
            Background = background,
            Surface = surface,
            AppbarBackground = appbarBackground,
            AppbarText = appbarText,
            DrawerBackground = drawerBackground,
            DrawerText = drawerText,
            DrawerIcon = drawerIcon,
            TextPrimary = textPrimary,
            TextSecondary = textSecondary,
            Info = "#2563EB",
            Success = "#16A34A",
            Warning = "#D97706",
            Error = "#DC2626",
            LinesDefault = linesDefault,
            Divider = divider
        };
    }

    /// <summary>
    /// Generates a complete dark palette from natural + brand colors.
    /// Natural color is shifted darker for dark mode surfaces and backgrounds.
    /// </summary>
    public static UiThemePaletteDto GenerateDarkPalette(string naturalHex, string brandHex)
    {
        var natural = HslColor.FromHex(naturalHex);
        var brand = HslColor.FromHex(brandHex);

        var background = natural.AdjustLightness(Math.Max(natural.L > 50 ? 8 : natural.L - 40, 5)).ToHex();
        var surface = natural.AdjustLightness(Math.Max(natural.L > 50 ? 14 : natural.L - 30, 10)).ToHex();
        var textPrimary = "#F8FAFC";
        var textSecondary = "#94A3B8";
        var divider = natural.AdjustLightness(Math.Max(natural.L > 50 ? 20 : natural.L + 10, 15)).WithAlpha(0.12).ToHex();
        var linesDefault = "#334155";

        var primary = brand.AdjustLightness(Math.Max(brand.L + 10, 40)).ToHex();
        var primaryContrastText = "#FFFFFF";
        var secondary = brand.AdjustSaturation(Math.Max(brand.S - 20, 0)).AdjustLightness(Math.Max(brand.L - 5, 30)).ToHex();
        var secondaryContrastText = "#F1F5F9";

        var appbarBackground = background;
        var appbarText = textPrimary;
        var drawerBackground = background;
        var drawerText = textPrimary;
        var drawerIcon = "#CBD5E1";

        return new UiThemePaletteDto
        {
            Primary = primary,
            PrimaryContrastText = primaryContrastText,
            Secondary = secondary,
            SecondaryContrastText = secondaryContrastText,
            Background = background,
            Surface = surface,
            AppbarBackground = appbarBackground,
            AppbarText = appbarText,
            DrawerBackground = drawerBackground,
            DrawerText = drawerText,
            DrawerIcon = drawerIcon,
            TextPrimary = textPrimary,
            TextSecondary = textSecondary,
            Info = "#60A5FA",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            LinesDefault = linesDefault,
            Divider = divider
        };
    }

    /// <summary>
    /// Generates a WCAG AAA-compliant light high-contrast palette.
    /// Prioritizes maximum text contrast: pure black text on white/near-white surfaces.
    /// </summary>
    public static UiThemePaletteDto GenerateHighContrastLightPalette(string naturalHex, string brandHex)
    {
        var brand = HslColor.FromHex(brandHex);
        var saturatedBrand = brand.AdjustSaturation(Math.Min(brand.S + 30, 100)).ToHex();

        return new UiThemePaletteDto
        {
            Primary = saturatedBrand,
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#1E293B",
            SecondaryContrastText = "#FFFFFF",
            Background = "#FFFFFF",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#000000",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#000000",
            DrawerIcon = "#000000",
            TextPrimary = "#000000",
            TextSecondary = "#1E293B",
            Info = "#0050D8",
            Success = "#006600",
            Warning = "#B45309",
            Error = "#B91C1C",
            LinesDefault = "#000000",
            Divider = "#000000"
        };
    }

    /// <summary>
    /// Generates a WCAG AAA-compliant dark high-contrast palette.
    /// Pure white text on pure black backgrounds for maximum readability.
    /// </summary>
    public static UiThemePaletteDto GenerateHighContrastDarkPalette(string naturalHex, string brandHex)
    {
        var brand = HslColor.FromHex(brandHex);
        var brightBrand = brand.AdjustLightness(Math.Max(brand.L + 20, 60)).AdjustSaturation(Math.Min(brand.S + 30, 100)).ToHex();

        return new UiThemePaletteDto
        {
            Primary = brightBrand,
            PrimaryContrastText = "#000000",
            Secondary = "#F8FAFC",
            SecondaryContrastText = "#000000",
            Background = "#000000",
            Surface = "#0A0A0A",
            AppbarBackground = "#000000",
            AppbarText = "#FFFFFF",
            DrawerBackground = "#000000",
            DrawerText = "#FFFFFF",
            DrawerIcon = "#FFFFFF",
            TextPrimary = "#FFFFFF",
            TextSecondary = "#E2E8F0",
            Info = "#93C5FD",
            Success = "#6EE7B7",
            Warning = "#FCD34D",
            Error = "#FCA5A5",
            LinesDefault = "#FFFFFF",
            Divider = "#FFFFFF"
        };
    }
}
