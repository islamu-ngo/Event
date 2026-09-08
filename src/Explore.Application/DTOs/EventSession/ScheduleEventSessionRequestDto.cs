namespace Explore.Application.DTOs.EventSession;

public sealed record ScheduleEventSessionRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
}
