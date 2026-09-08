namespace Explore.Application.Services;

using Explore.Domain.ValueObjects;

public static class EmergencyFallbackPalettes
{
    public static UiThemePalette FallbackLight => new()
    {
        Primary = "#18181B",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#52525B",
        SecondaryContrastText = "#FFFFFF",
        Background = "#F5F5F7",
        Surface = "#FFFFFF",
        AppbarBackground = "#FFFFFF",
        AppbarText = "#18181B",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#18181B",
        DrawerIcon = "#52525B",
        TextPrimary = "#18181B",
        TextSecondary = "#404040",
        Info = "#52525B",
        Success = "#16A34A",
        Warning = "#D97706",
        Error = "#DC2626",
        LinesDefault = "#A1A1AA",
        Divider = "#E4E4E7"
    };

    public static UiThemePalette FallbackDark => new()
    {
        Primary = "#FAFAFA",
        PrimaryContrastText = "#1A1A1A",
        Secondary = "#A1A1AA",
        SecondaryContrastText = "#1A1A1A",
        Background = "#1A1A1A",
        Surface = "#242424",
        AppbarBackground = "rgba(26,26,26,0.92)",
        AppbarText = "#FAFAFA",
        DrawerBackground = "#1A1A1A",
        DrawerText = "#FAFAFA",
        DrawerIcon = "#A1A1AA",
        TextPrimary = "#FAFAFA",
        TextSecondary = "#A1A1AA",
        Info = "#A1A1AA",
        Success = "#34D399",
        Warning = "#FBBF24",
        Error = "#F87171",
        LinesDefault = "#3F3F46",
        Divider = "#2E2E2E"
    };

    public static UiThemePalette FallbackLightHighContrast => new()
    {
        Primary = "#0050D8",
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

    public static UiThemePalette FallbackDarkHighContrast => new()
    {
        Primary = "#93C5FD",
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
