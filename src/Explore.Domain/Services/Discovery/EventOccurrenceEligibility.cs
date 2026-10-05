using System.Linq.Expressions;
using Explore.Domain.Enums;

namespace Explore.Domain.Services.Discovery;

public static class EventOccurrenceEligibility
{
    public static Expression<Func<EventSession, bool>> Published() =>
        session => !session.IsDeleted
            && session.EventSessionStatusId == (int)EventSessionStatusEnum.Published
            && session.StartTime != null
            && session.LocalStartDate != null
            && (session.EndTime > session.StartTime
                || session.EndTime == null && session.EndTimeType == SessionEndTimeType.OpenEnded)
            && (session.EventDayId == null
                || session.EventDay != null
                    && !session.EventDay.IsDeleted
                    && session.EventDay.IsPublished
                    && session.EventDay.EventId == session.EventId
                    && session.EventDay.TenantId == session.TenantId
                    && session.EventDay.LocalDate == session.LocalStartDate);

    /// <summary>
    /// Intersects an inclusive local date window with a half-open occurrence interval.
    /// Local projections are maintained by the event's scheduling timezone authority.
    /// An end at local midnight does not occupy that date.
    /// </summary>
    public static Expression<Func<EventSession, bool>> WithinDates(DateOnly? dateFrom, DateOnly? dateTo) =>
        session => (!dateTo.HasValue || session.LocalStartDate <= dateTo)
            && (!dateFrom.HasValue
                || session.EndTime == null && session.EndTimeType == SessionEndTimeType.OpenEnded
                || session.LocalEndDate > dateFrom
                || session.LocalEndDate == dateFrom && session.LocalEndTime > TimeOnly.MinValue);

    public static Expression<Func<EventSession, bool>> Upcoming(DateTimeOffset now) =>
        session => session.StartTime > now;

    public static Expression<Func<EventSession, bool>> Ongoing(DateTimeOffset now) =>
        session => session.StartTime <= now
            && (session.EndTime > now
                || session.EndTime == null && session.EndTimeType == SessionEndTimeType.OpenEnded);

    public static Expression<Func<EventSession, bool>> Past(DateTimeOffset now) =>
        session => session.EndTime <= now;

    public static Expression<Func<EventSession, bool>> CurrentOrUpcoming(DateTimeOffset now) =>
        session => session.EndTime > now
            || session.EndTime == null && session.EndTimeType == SessionEndTimeType.OpenEnded;
}
