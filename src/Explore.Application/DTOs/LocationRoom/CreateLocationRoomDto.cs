namespace Explore.Application.DTOs.LocationRoom;

public sealed record CreateLocationRoomDto
{
    public Guid LocationId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public int? Capacity { get; init; }
    public int SortOrder { get; init; }
}
