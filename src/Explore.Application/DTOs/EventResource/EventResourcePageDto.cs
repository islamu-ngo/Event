using System.Collections.Immutable;

namespace Explore.Application.DTOs.EventResource;

/// <summary>A complete authorized page without an invented total or an exposed candidate position.</summary>
public sealed record EventResourcePageDto(
    ImmutableArray<EventResourceAudienceDetailDto> Items, string? NextCursor)
{
    public override string ToString() => nameof(EventResourcePageDto);
}
