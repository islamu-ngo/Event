using Explore.Domain.Enums;

namespace Explore.Domain.Services.Lifecycle;

public static class EventLifecycleRules
{
    public static bool IsDefinedStatus(EventStatusEnum status) => Enum.IsDefined(status);

    public static bool IsDraftEditable(EventStatusEnum status) => status == EventStatusEnum.Draft;

    public static bool CanRestoreAfterLightModeration(EventStatusEnum status) => status == EventStatusEnum.Moderated;

    public static bool CanTransition(EventStatusEnum current, EventStatusEnum desired)
    {
        if (!IsDefinedStatus(current) || !IsDefinedStatus(desired))
        {
            return false;
        }

        return current == desired || (current, desired) switch
        {
            (EventStatusEnum.Draft, EventStatusEnum.Published or EventStatusEnum.Cancelled or EventStatusEnum.Archived) => true,
            (EventStatusEnum.Published, EventStatusEnum.Cancelled or EventStatusEnum.Moderated) => true,
            (EventStatusEnum.Cancelled, EventStatusEnum.Archived) => true,
            (EventStatusEnum.Completed, EventStatusEnum.Archived) => true,
            _ => false
        };
    }

    public static void EnsureDefinedStatus(EventStatusEnum status, string parameterName)
    {
        if (!IsDefinedStatus(status))
        {
            throw new ArgumentException("Event status is not defined.", parameterName);
        }
    }

    public static void EnsureCanTransition(EventStatusEnum current, EventStatusEnum desired)
    {
        if (!CanTransition(current, desired))
        {
            throw new InvalidOperationException($"Event cannot transition from {current} to {desired}.");
        }
    }

    public static void EnsureDraftEditable(EventStatusEnum status)
    {
        EnsureDefinedStatus(status, nameof(status));
        if (!IsDraftEditable(status))
        {
            throw new InvalidOperationException("Only draft events are editable through draft-only mutation paths.");
        }
    }
}
