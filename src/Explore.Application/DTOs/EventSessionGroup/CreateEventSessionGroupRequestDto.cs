namespace Explore.Application.DTOs.EventSessionGroup;

public sealed record CreateEventSessionGroupRequestDto
{
    public Guid EventId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? RoomId { get; init; }
    public string? Color { get; init; }
    public int SortOrder { get; init; }
    public bool IsPublished { get; init; }
}
