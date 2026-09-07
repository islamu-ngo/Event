namespace Explore.Application.DTOs.Appearance;

public record class UserAppearancePreferencesDto
{
    public string ThemeMode { get; set; } = "system";
    public string Direction { get; set; } = "auto";
    public string Language { get; set; } = "en";
    public Guid? DefaultThemeId { get; set; }
}
