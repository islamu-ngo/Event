using System;

namespace Explore.Application.DTOs.Event;

public sealed record CreateEventGraphAgendaItemDto
{
    public string? TempKey { get; init; }
    public string? DayTempKey { get; init; }
    public string? RoomTempKey { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public Guid? LocationId { get; init; }
    public string? LocationTempKey { get; init; }
    public Guid? RoomId { get; init; }
    public int? KindId { get; init; }
    public int SortOrder { get; init; }
}
