// ABOUTME: DTO wrapping a category's worth of effective settings for API responses.
// ABOUTME: Generic across all setting categories — EventList, Appearance, Branding, etc.

namespace Explore.Application.DTOs.Settings;

/// <summary>
/// Response containing all effective settings for a given category,
/// resolved through the hierarchical cascade for the requesting scope.
/// </summary>
public sealed record SettingGroupResponseDto
{
    public Guid? TenantId { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<EffectiveSettingDto> Settings { get; init; }
}
