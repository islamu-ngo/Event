namespace Explore.Domain;

using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;

public class EventSessionIslamicAspect
{
    public const int MinOffsetMinutes = -180;
    public const int MaxOffsetMinutes = 180;

    /// <summary>
    /// Shared key with EventSession.Id (PK + FK).
    /// </summary>
    [ForeignKey(nameof(EventSession))]
    public Guid EventSessionId { get; set; }
    public EventSession? EventSession { get; set; }

    /// <summary>
    /// Session start-time strategy for Islamic scheduling.
    /// </summary>
    public SessionStartTimeType StartTimeType { get; set; } = SessionStartTimeType.RelativeToPrayer;

    /// <summary>
    /// Prayer reference used when StartTimeType is RelativeToPrayer.
    /// </summary>
    public PrayerTime? ReferencePrayer { get; set; }

    /// <summary>
    /// Offset in minutes from the referenced prayer.
    /// Positive = after prayer, negative = before prayer.
    /// </summary>
    public int? OffsetMinutes { get; set; }

    /// <summary>
    /// Prayer reference used when EndTimeType is RelativeToPrayer.
    /// </summary>
    public PrayerTime? EndReferencePrayer { get; set; }

    /// <summary>
    /// Offset in minutes from the referenced end prayer.
    /// Positive = after prayer, negative = before prayer.
    /// </summary>
    public int? EndOffsetMinutes { get; set; }

    /// <summary>
    /// Indicates whether this session requires participants to have wudu.
    /// </summary>
    public bool RequiresWudu { get; set; }

    /// <summary>
    /// Optional JSON payload for module-specific ritual requirements.
    /// </summary>
    public string? RitualRequirementsJson { get; set; }

    public void ApplyScheduling(
        SessionStartTimeType startTimeType,
        PrayerTime? referencePrayer,
        int? offsetMinutes)
    {
        switch (startTimeType)
        {
            case SessionStartTimeType.Fixed:
                if (referencePrayer.HasValue || offsetMinutes.HasValue)
                {
                    throw new ArgumentException("Fixed Islamic session scheduling must not include prayer reference fields.", nameof(startTimeType));
                }

                StartTimeType = SessionStartTimeType.Fixed;
                ReferencePrayer = null;
                OffsetMinutes = null;
                return;

            case SessionStartTimeType.RelativeToPrayer:
                if (!referencePrayer.HasValue || !offsetMinutes.HasValue)
                {
                    throw new ArgumentException("Prayer-relative Islamic session scheduling requires ReferencePrayer and OffsetMinutes.", nameof(startTimeType));
                }

                ValidateOffset(offsetMinutes.Value);
                StartTimeType = SessionStartTimeType.RelativeToPrayer;
                ReferencePrayer = referencePrayer.Value;
                OffsetMinutes = offsetMinutes.Value;
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(startTimeType), startTimeType, "Unsupported Islamic session start-time type.");
        }
    }

    public void ApplyEndTimeScheduling(
        SessionEndTimeType endTimeType,
        PrayerTime? endReferencePrayer,
        int? endOffsetMinutes)
    {
        switch (endTimeType)
        {
            case SessionEndTimeType.Fixed:
            case SessionEndTimeType.OpenEnded:
                if (endReferencePrayer.HasValue || endOffsetMinutes.HasValue)
                {
                    throw new ArgumentException("Fixed or OpenEnded Islamic session scheduling must not include prayer reference fields.", nameof(endTimeType));
                }

                EndReferencePrayer = null;
                EndOffsetMinutes = null;
                return;

            case SessionEndTimeType.RelativeToPrayer:
                if (!endReferencePrayer.HasValue || !endOffsetMinutes.HasValue)
                {
                    throw new ArgumentException("Prayer-relative Islamic session scheduling requires EndReferencePrayer and EndOffsetMinutes.", nameof(endTimeType));
                }

                ValidateOffset(endOffsetMinutes.Value);
                EndReferencePrayer = endReferencePrayer.Value;
                EndOffsetMinutes = endOffsetMinutes.Value;
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(endTimeType), endTimeType, "Unsupported Islamic session end-time type.");
        }
    }

    public static bool IsValidSchedulingState(
        SessionStartTimeType startTimeType,
        PrayerTime? referencePrayer,
        int? offsetMinutes)
    {
        return startTimeType switch
        {
            SessionStartTimeType.Fixed => !referencePrayer.HasValue && !offsetMinutes.HasValue,
            SessionStartTimeType.RelativeToPrayer => referencePrayer.HasValue
                && offsetMinutes.HasValue
                && offsetMinutes.Value is >= MinOffsetMinutes and <= MaxOffsetMinutes,
            _ => false
        };
    }

    public static bool IsValidEndTimeSchedulingState(
        SessionEndTimeType endTimeType,
        PrayerTime? endReferencePrayer,
        int? endOffsetMinutes)
    {
        return endTimeType switch
        {
            SessionEndTimeType.Fixed => !endReferencePrayer.HasValue && !endOffsetMinutes.HasValue,
            SessionEndTimeType.OpenEnded => !endReferencePrayer.HasValue && !endOffsetMinutes.HasValue,
            SessionEndTimeType.RelativeToPrayer => endReferencePrayer.HasValue
                && endOffsetMinutes.HasValue
                && endOffsetMinutes.Value is >= MinOffsetMinutes and <= MaxOffsetMinutes,
            _ => false
        };
    }

    private static void ValidateOffset(int offsetMinutes)
    {
        if (offsetMinutes is < MinOffsetMinutes or > MaxOffsetMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsetMinutes),
                offsetMinutes,
                $"Prayer-relative offset must be between {MinOffsetMinutes} and {MaxOffsetMinutes} minutes.");
        }
    }
}
