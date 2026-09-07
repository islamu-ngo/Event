namespace Explore.Application.DTOs.EventAgendaItem;

public sealed record CreateEventAgendaItemDto
{
    public Guid EventId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? RoomId { get; init; }
    public int? KindId { get; init; }
    public int SortOrder { get; init; }
}
