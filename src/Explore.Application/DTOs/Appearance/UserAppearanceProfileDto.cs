namespace Explore.Application.DTOs.Appearance;

public sealed record UserAppearanceProfileDto
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ThemeMode { get; init; } = "system";
    public required UiThemePaletteDto LightPaletteSnapshot { get; init; }
    public required UiThemePaletteDto DarkPaletteSnapshot { get; init; }
    public string? SourcePresetKey { get; init; }
    public Guid? SourcePresetId { get; init; }
    public int? SourcePresetSeedVersion { get; init; }
    public bool IsUserEditable { get; init; }
    public bool IsDefault { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? ClonedAt { get; init; }
}
