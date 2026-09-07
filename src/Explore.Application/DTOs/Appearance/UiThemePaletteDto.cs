namespace Explore.Application.DTOs.Appearance;

public sealed record UiThemePaletteDto
{
    public string Primary { get; init; } = string.Empty;
    public string PrimaryContrastText { get; init; } = "#FFFFFF";
    public string Secondary { get; init; } = string.Empty;
    public string SecondaryContrastText { get; init; } = "#FFFFFF";
    public string Background { get; init; } = string.Empty;
    public string Surface { get; init; } = string.Empty;
    public string AppbarBackground { get; init; } = string.Empty;
    public string AppbarText { get; init; } = string.Empty;
    public string DrawerBackground { get; init; } = string.Empty;
    public string DrawerText { get; init; } = string.Empty;
    public string DrawerIcon { get; init; } = string.Empty;
    public string TextPrimary { get; init; } = string.Empty;
    public string TextSecondary { get; init; } = string.Empty;
    public string Info { get; init; } = string.Empty;
    public string Success { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string LinesDefault { get; init; } = string.Empty;
    public string Divider { get; init; } = string.Empty;
}
