namespace Explore.Application.DTOs.Appearance;

using Explore.Application.Models.Common;

public sealed record UpdateUiThemeDto
{
    public required uint RowVersion { get; init; }
    public UpdateUiThemeMetadataDto? Metadata { get; init; }
    public UpdateUiThemeStateDto? State { get; init; }
    public UpdateUiThemePalettesDto? Palettes { get; init; }
}

public sealed record UpdateUiThemeMetadataDto
{
    public string? ThemeKey { get; init; }
    public string? DisplayName { get; init; }
    public OptionalUpdate<string> Description { get; init; } = OptionalUpdate<string>.Unspecified();
}

public sealed record UpdateUiThemeStateDto
{
    public bool? IsActive { get; init; }
    public bool? IsDefault { get; init; }
    public int? SortOrder { get; init; }
}

public sealed record UpdateUiThemePalettesDto
{
    public UiThemePaletteDto? Light { get; init; }
    public UiThemePaletteDto? Dark { get; init; }
}
