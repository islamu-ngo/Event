namespace Explore.Application.DTOs.Settings;

/// <summary>
/// Response containing all effective settings for a given category,
/// resolved through the hierarchical cascade for the requesting scope.
/// </summary>
public sealed record SettingGroupResponseDto
{
    public required string Category { get; init; }
    public required IReadOnlyList<EffectiveSettingDto> Settings { get; init; }
}
