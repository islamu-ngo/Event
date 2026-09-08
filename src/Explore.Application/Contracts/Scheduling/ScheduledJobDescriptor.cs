namespace Explore.Application.Contracts.Scheduling;

public sealed record ScheduledJobDescriptor(
    string Name,
    string Owner,
    ScheduledJobScheduleKind ScheduleKind,
    ScheduledJobPayloadKind PayloadKind,
    ScheduledJobOperationalStatus Status,
    string Purpose,
    string? CronExpression = null);

public enum ScheduledJobScheduleKind
{
    Cron = 1,
    Time = 2,
    Operator = 3,
    Interval = 4
}

public enum ScheduledJobPayloadKind
{
    None = 1,
    PointerOnly = 2
}

public enum ScheduledJobOperationalStatus
{
    Implemented = 1,
    Planned = 2
}
