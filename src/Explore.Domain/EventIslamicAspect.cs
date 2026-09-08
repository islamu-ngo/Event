// Uses 1:1 shared primary key pattern where Id is both PK and FK to Event.

namespace Explore.Domain;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Islamic-specific aspects for events. Uses shared primary key pattern (Id = Event.Id).
/// Only created when an event has Islamic characteristics.
/// </summary>
public class EventIslamicAspect
{
    /// <summary>
    /// Primary key, also foreign key to Event.Id (shared PK pattern).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Navigation property to the parent event.
    /// </summary>
    public Event? Event { get; set; }

    /// <summary>
    /// Islamic school of jurisprudence for this event.
    /// </summary>
    [ForeignKey("Madhab")]
    public int? MadhabId { get; set; }

    /// <summary>
    /// Navigation property to Madhab lookup.
    /// </summary>
    public Madhab? Madhab { get; set; }

    /// <summary>
    /// Reference prayer for scheduling (Fajr, Dhuhr, Asr, Maghrib, Isha).
    /// Null means absolute time is used.
    /// </summary>
    public PrayerTime? ReferencePrayer { get; set; }

    /// <summary>
    /// Minutes offset from reference prayer time.
    /// Positive = after prayer, Negative = before prayer.
    /// </summary>
    public int? PrayerTimeOffset { get; set; }

    /// <summary>
    /// Gender segregation mode for the event.
    /// </summary>
    public GenderSegregationMode GenderMode { get; set; } = GenderSegregationMode.Mixed;

    /// <summary>
    /// Whether the event includes Quran recitation.
    /// </summary>
    public bool IncludesQuranRecitation { get; set; }

    /// <summary>
    /// Primary language for Islamic content (Arabic, etc.).
    /// </summary>
    [ForeignKey("PrimaryLanguage")]
    public int? PrimaryLanguageId { get; set; }

    /// <summary>
    /// Navigation property to primary language.
    /// </summary>
    public Language? PrimaryLanguage { get; set; }
}

/// <summary>
/// Prayer times for scheduling events relative to prayers.
/// </summary>
public enum PrayerTime
{
    Fajr = 1,
    Sunrise = 2,
    Dhuhr = 3,
    Asr = 4,
    Maghrib = 5,
    Isha = 6
}

/// <summary>
/// Gender segregation modes for Islamic events.
/// </summary>
public enum GenderSegregationMode
{
    /// <summary>Mixed attendance (no segregation).</summary>
    Mixed = 0,
    /// <summary>Men only event.</summary>
    MenOnly = 1,
    /// <summary>Women only event.</summary>
    WomenOnly = 2,
    /// <summary>Segregated sections for men and women.</summary>
    Segregated = 3,
    /// <summary>Family-oriented with family seating.</summary>
    Family = 4
}
