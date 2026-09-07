namespace Explore.Application.DTOs.Appearance;

public sealed record SetThemeModeRequestDto
{
    public string ThemeMode { get; init; } = "system";
}
