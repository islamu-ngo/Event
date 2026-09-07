namespace Explore.Application.DTOs.Appearance;

public sealed record UpdateAppearanceProfileRequestDto
{
    public UpdateAppearanceProfileMetadataDto? Metadata { get; init; }
    public UpdateAppearanceProfilePalettesDto? Palettes { get; init; }
}

public sealed record UpdateAppearanceProfileMetadataDto
{
    public string? Name { get; init; }
    public string? ThemeMode { get; init; }
}

public sealed record UpdateAppearanceProfilePalettesDto
{
    public UiThemePaletteDto? Light { get; init; }
    public UiThemePaletteDto? Dark { get; init; }
}
