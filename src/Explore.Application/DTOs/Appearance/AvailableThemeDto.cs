namespace Explore.Application.DTOs.Appearance;

public sealed record AvailableThemeDto
{
    public Guid Id { get; init; }
    public string ThemeKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsDefault { get; init; }
    public bool IsPlatformTheme { get; init; }
    public int SortOrder { get; init; }
    public required UiThemePaletteDto LightPalette { get; init; }
    public required UiThemePaletteDto DarkPalette { get; init; }
}
