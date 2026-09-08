using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventAgendaItem;

public sealed record EventAgendaItemListDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public DateOnly LocalStartDate { get; init; }
    public TimeOnly LocalStartTime { get; init; }
    public TimeOnly LocalEndTime { get; init; }
    public int? KindId { get; init; }
    public string? KindFullName { get; init; }
    public int SortOrder { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public EventLocationPublicDto? EventLocation { get; set; }
}
