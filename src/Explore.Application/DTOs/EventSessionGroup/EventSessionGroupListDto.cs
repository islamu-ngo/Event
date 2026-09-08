using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventSessionGroup;

public sealed record EventSessionGroupListDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public Guid? RoomId { get; set; }
    public string? RoomName { get; set; }
    public EventLocationPublicDto? EventLocation { get; set; }
    public string? Color { get; init; }
    public int SortOrder { get; init; }
    public bool IsPublished { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
