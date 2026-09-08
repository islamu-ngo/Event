using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.LocationRoom;

public sealed record UpdateLocationRoomDto
{
    public UpdateLocationRoomLocationDto? Location { get; init; }
    public UpdateLocationRoomNameDto? Name { get; init; }
    public UpdateLocationRoomSlugDto? Slug { get; init; }
    public UpdateLocationRoomDescriptionDto? Description { get; init; }
    public UpdateLocationRoomCapacityDto? Capacity { get; init; }
    public UpdateLocationRoomSortOrderDto? SortOrder { get; init; }
}

public sealed record UpdateLocationRoomLocationDto
{
    public Guid LocationId { get; init; }
}

public sealed record UpdateLocationRoomNameDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationRoomSlugDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateLocationRoomDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateLocationRoomCapacityDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateLocationRoomSortOrderDto
{
    public int Value { get; init; }
}
