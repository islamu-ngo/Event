namespace Explore.Application.DTOs.Appearance;

public sealed record ClonePresetRequestDto
{
    /// <summary>Optional name override for the cloned profile. Defaults to the preset's display name.</summary>
    public string? Name { get; init; }
}
