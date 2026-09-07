namespace Explore.Application.DTOs.Appearance;

public sealed record UpdateUserAppearancePreferencesDto
{
    public UpdateAppearanceLocalizationDto? Localization { get; init; }
}

public sealed record UpdateAppearanceLocalizationDto
{
    public string? Direction { get; init; }
    public string? Language { get; init; }
}
