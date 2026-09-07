namespace Explore.Application.DTOs.LocationRoom;

public sealed record LocationRoomDto
{
    public Guid Id { get; init; }
    public Guid LocationId { get; init; }
    public string? LocationFullName { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public int? Capacity { get; init; }
    public int SortOrder { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
