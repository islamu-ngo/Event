namespace Explore.Application.DTOs.EventAspects;

using Explore.Application.Models.Common;
using Explore.Domain;

/// <summary>
/// DTO representing the Islamic aspect of an event.
/// </summary>
public sealed record EventIslamicAspectDto
{
    public int? MadhabId { get; init; }
    public string? MadhabName { get; init; }
    public PrayerTime? ReferencePrayer { get; init; }
    public int? PrayerTimeOffset { get; init; }
    public GenderSegregationMode GenderMode { get; init; }
    public string GenderModeName => GenderMode.ToString();
    public bool IncludesQuranRecitation { get; init; }
    public int? PrimaryLanguageId { get; init; }
    public string? PrimaryLanguageName { get; init; }
}

/// <summary>
/// DTO for creating or updating the Islamic aspect of an event.
/// </summary>
public sealed record CreateUpdateIslamicAspectDto
{
    public int? MadhabId { get; init; }
    public PrayerTime? ReferencePrayer { get; init; }
    public int? PrayerTimeOffset { get; init; }
    public GenderSegregationMode GenderMode { get; init; } = GenderSegregationMode.Mixed;
    public bool IncludesQuranRecitation { get; init; }
    public int? PrimaryLanguageId { get; init; }
}

public sealed record UpdateEventIslamicAspectDto
{
    public UpdateEventIslamicJurisprudenceDto? Jurisprudence { get; init; }
    public UpdateEventIslamicPrayerScheduleDto? PrayerSchedule { get; init; }
    public UpdateEventIslamicParticipationDto? Participation { get; init; }
    public UpdateEventIslamicLanguageDto? Language { get; init; }
}

public sealed record UpdateEventIslamicJurisprudenceDto
{
    public OptionalUpdate<int?> MadhabId { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventIslamicPrayerScheduleDto
{
    public OptionalUpdate<PrayerTime?> ReferencePrayer { get; init; } = OptionalUpdate<PrayerTime?>.Unspecified();
    public OptionalUpdate<int?> PrayerTimeOffset { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventIslamicParticipationDto
{
    public GenderSegregationMode? GenderMode { get; init; }
    public bool? IncludesQuranRecitation { get; init; }
}

public sealed record UpdateEventIslamicLanguageDto
{
    public OptionalUpdate<int?> PrimaryLanguageId { get; init; } = OptionalUpdate<int?>.Unspecified();
}
