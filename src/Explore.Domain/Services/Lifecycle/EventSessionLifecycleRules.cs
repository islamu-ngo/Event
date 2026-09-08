using Explore.Domain.Enums;

namespace Explore.Domain.Services.Lifecycle;

public static class EventSessionLifecycleRules
{
    public static bool IsDefinedStatus(EventSessionStatusEnum status) => Enum.IsDefined(status);

    public static bool CanSchedule(EventSessionStatusEnum current) => IsDefinedStatus(current) && current is
        EventSessionStatusEnum.Draft or
        EventSessionStatusEnum.Submitted or
        EventSessionStatusEnum.UnderReview or
        EventSessionStatusEnum.Approved or
        EventSessionStatusEnum.Published;

    public static bool CanPublish(
        EventSessionStatusEnum current,
        EventStatusEnum parent,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        SessionEndTimeType endTimeType) =>
        IsDefinedStatus(current) &&
        current is (EventSessionStatusEnum.Draft or EventSessionStatusEnum.Submitted or EventSessionStatusEnum.UnderReview or EventSessionStatusEnum.Approved) &&
        IsPublishParentCompatible(parent) &&
        HasPublishableSchedule(startTime, endTime, endTimeType);

    public static bool IsPublishParentCompatible(EventStatusEnum parent) =>
        EventLifecycleRules.IsDefinedStatus(parent) && parent == EventStatusEnum.Published;

    public static bool HasPublishableSchedule(
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        SessionEndTimeType endTimeType) =>
        startTime is not null && endTimeType switch
        {
            SessionEndTimeType.Fixed => endTime > startTime,
            SessionEndTimeType.OpenEnded => endTime is null,
            SessionEndTimeType.RelativeToPrayer => endTime is null || endTime > startTime,
            _ => false
        };

    public static bool CanCancel(EventSessionStatusEnum current, EventStatusEnum parent) =>
        IsDefinedStatus(current) &&
        IsMutableParent(parent) &&
        current is EventSessionStatusEnum.Draft or EventSessionStatusEnum.Submitted or EventSessionStatusEnum.UnderReview or EventSessionStatusEnum.Approved or EventSessionStatusEnum.Published;

    public static bool CanComplete(EventSessionStatusEnum current, EventStatusEnum parent) =>
        current == EventSessionStatusEnum.Published && parent == EventStatusEnum.Published;

    public static bool CanArchive(EventSessionStatusEnum current, EventStatusEnum parent) =>
        IsDefinedStatus(current) &&
        IsMutableParent(parent) &&
        current is EventSessionStatusEnum.Draft or EventSessionStatusEnum.Cancelled or EventSessionStatusEnum.Completed;

    public static void EnsureDefinedStatus(EventSessionStatusEnum status, string parameterName)
    {
        if (!IsDefinedStatus(status))
        {
            throw new ArgumentException("Event session status is not defined.", parameterName);
        }
    }

    public static void EnsureCanSchedule(EventSessionStatusEnum current)
    {
        if (!CanSchedule(current))
        {
            throw new InvalidOperationException($"Event session cannot be scheduled from {current}.");
        }
    }

    public static void EnsureCanPublish(
        EventSessionStatusEnum current,
        EventStatusEnum parent,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        SessionEndTimeType endTimeType)
    {
        if (!CanPublish(current, parent, startTime, endTime, endTimeType))
        {
            throw new InvalidOperationException($"Event session cannot be published from {current} while parent event is {parent}.");
        }
    }

    public static void EnsureCanCancel(EventSessionStatusEnum current, EventStatusEnum parent)
    {
        if (!CanCancel(current, parent))
        {
            throw new InvalidOperationException($"Event session cannot be cancelled from {current} while parent event is {parent}.");
        }
    }

    public static void EnsureCanComplete(EventSessionStatusEnum current, EventStatusEnum parent)
    {
        if (!CanComplete(current, parent))
        {
            throw new InvalidOperationException($"Event session cannot be completed from {current} while parent event is {parent}.");
        }
    }

    public static void EnsureCanArchive(EventSessionStatusEnum current, EventStatusEnum parent)
    {
        if (!CanArchive(current, parent))
        {
            throw new InvalidOperationException($"Event session cannot be archived from {current} while parent event is {parent}.");
        }
    }

    private static bool IsMutableParent(EventStatusEnum parent) =>
        EventLifecycleRules.IsDefinedStatus(parent) && parent is not (EventStatusEnum.Moderated or EventStatusEnum.Archived);
}
