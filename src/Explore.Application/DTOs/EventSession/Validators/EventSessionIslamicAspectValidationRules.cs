using System;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventSession.Validators;

public static class EventSessionIslamicAspectValidationRules
{
    public const string SchedulingStateMessage =
        "Islamic session scheduling must be Fixed without ReferencePrayer/OffsetMinutes, or RelativeToPrayer with LocationId, ReferencePrayer, and OffsetMinutes.";

    public const string OffsetRangeMessage =
        "Islamic session prayer offset must be between -180 and 180 minutes.";

    public static bool HasValidSchedulingState(
        EventSessionIslamicAspectDto? aspect,
        Guid? locationId)
    {
        if (aspect is null)
        {
            return true;
        }

        var startValid = EventSessionIslamicAspect.IsValidSchedulingState(
            aspect.StartTimeType,
            aspect.ReferencePrayer,
            aspect.OffsetMinutes);

        var endTimeType = aspect.EndReferencePrayer.HasValue || aspect.EndOffsetMinutes.HasValue
            ? SessionEndTimeType.RelativeToPrayer
            : SessionEndTimeType.Fixed;

        var endValid = EventSessionIslamicAspect.IsValidEndTimeSchedulingState(
            endTimeType,
            aspect.EndReferencePrayer,
            aspect.EndOffsetMinutes);

        return startValid && endValid && (aspect.StartTimeType != SessionStartTimeType.RelativeToPrayer || locationId.HasValue);
    }
}
