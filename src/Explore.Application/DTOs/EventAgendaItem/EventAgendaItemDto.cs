using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventAgendaItem;

public sealed record EventAgendaItemDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public string? EventTitle { get; init; }
    public Guid? EventDayId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }

    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }

    public DateOnly LocalStartDate { get; init; }
    public DateOnly LocalEndDate { get; init; }
    public TimeOnly LocalStartTime { get; init; }
    public TimeOnly LocalEndTime { get; init; }
    public int LocalStartMinuteOfDay { get; init; }
    public int LocalEndMinuteOfDay { get; init; }

    public Guid? LocationId { get; set; }
    public Guid? RoomId { get; set; }
    public EventLocationPublicDto? EventLocation { get; set; }
    public int? KindId { get; init; }
    public string? KindFullName { get; init; }
    public int SortOrder { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
