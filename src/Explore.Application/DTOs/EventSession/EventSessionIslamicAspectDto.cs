// Captures prayer-relative scheduling and ritual requirements per session.

namespace Explore.Application.DTOs.EventSession;

using Explore.Domain;

public sealed record EventSessionIslamicAspectDto
{
    public SessionStartTimeType StartTimeType { get; init; } = SessionStartTimeType.RelativeToPrayer;
    public PrayerTime? ReferencePrayer { get; init; }
    public int? OffsetMinutes { get; init; }
    public PrayerTime? EndReferencePrayer { get; init; }
    public int? EndOffsetMinutes { get; init; }
    public bool RequiresWudu { get; init; }
    public string? RitualRequirementsJson { get; init; }
}
