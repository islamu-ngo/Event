namespace Explore.Application.Services;

using Explore.Application.DTOs.Appearance;

internal static class AppearanceMapper
{
    internal static UiThemePaletteDto ToPaletteDto(Domain.ValueObjects.UiThemePalette palette) => new()
    {
        Primary = palette.Primary,
        PrimaryContrastText = palette.PrimaryContrastText,
        Secondary = palette.Secondary,
        SecondaryContrastText = palette.SecondaryContrastText,
        Background = palette.Background,
        Surface = palette.Surface,
        AppbarBackground = palette.AppbarBackground,
        AppbarText = palette.AppbarText,
        DrawerBackground = palette.DrawerBackground,
        DrawerText = palette.DrawerText,
        DrawerIcon = palette.DrawerIcon,
        TextPrimary = palette.TextPrimary,
        TextSecondary = palette.TextSecondary,
        Info = palette.Info,
        Success = palette.Success,
        Warning = palette.Warning,
        Error = palette.Error,
        LinesDefault = palette.LinesDefault,
        Divider = palette.Divider
    };
}
