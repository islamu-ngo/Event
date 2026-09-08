using System;

namespace Explore.Domain.Services.Scheduling;

public readonly record struct LocalScheduleProjection(
    DateOnly LocalStartDate,
    DateOnly? LocalEndDate,
    TimeOnly LocalStartTime,
    TimeOnly? LocalEndTime,
    int LocalStartMinuteOfDay,
    int? LocalEndMinuteOfDay);
