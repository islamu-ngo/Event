using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventSessionGroup;

public sealed record UpdateEventSessionGroupRequestDto
{
    public UpdateEventSessionGroupMetadataDto? Metadata { get; init; }
    public UpdateEventSessionGroupPlacementDto? Placement { get; init; }
    public UpdateEventSessionGroupOrderingDto? Ordering { get; init; }
    public UpdateEventSessionGroupPublicationDto? Publication { get; init; }
}

public sealed record UpdateEventSessionGroupMetadataDto
{
    public string? Name { get; init; }
    public OptionalUpdate<string?> Slug { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> Description { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> Color { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSessionGroupPlacementDto
{
    public OptionalUpdate<Guid?> LocationId { get; init; } = OptionalUpdate<Guid?>.Unspecified();
    public OptionalUpdate<Guid?> RoomId { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSessionGroupOrderingDto
{
    public int? SortOrder { get; init; }
}

public sealed record UpdateEventSessionGroupPublicationDto
{
    public bool? IsPublished { get; init; }
}
