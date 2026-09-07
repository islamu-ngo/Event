namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Stable product-concept field keys used in event readiness and validation errors.
/// These map to user-facing or API-contract field paths, not internal CLR property names.
/// </summary>
public enum EventFieldKey
{
    /// <summary>Event title / name.</summary>
    Title,

    /// <summary>Event description / long-form content.</summary>
    Description,

    /// <summary>Event cover or primary image.</summary>
    CoverImage,

    /// <summary>Schedule timezone IANA identifier (e.g. Europe/Brussels).</summary>
    ScheduleTimeZone,

    /// <summary>At least one scheduled session exists.</summary>
    ScheduleSessions,

    /// <summary>First session start UTC instant.</summary>
    ScheduleFirstStart,

    /// <summary>Last session end UTC instant.</summary>
    ScheduleLastEnd,

    /// <summary>Event visibility lookup (public/private/link-only).</summary>
    Visibility,

    /// <summary>Event format lookup (in-person/online/hybrid).</summary>
    Format,

    /// <summary>Event type lookup (conference/workshop/etc).</summary>
    Type,

    /// <summary>Target audience gender lookup.</summary>
    AudienceGender,

    /// <summary>Target audience age range lookup.</summary>
    AudienceAge,

    /// <summary>Owner actor reference.</summary>
    Owner,

    /// <summary>Tenant scope reference.</summary>
    Tenant,

    /// <summary>Current lifecycle status lookup.</summary>
    Status,

    /// <summary>Physical location reference (optional for online events).</summary>
    Location,

    /// <summary>Import/archive provenance source identifier.</summary>
    ProvenanceSource,

    /// <summary>Import/archive provenance external identifier.</summary>
    ProvenanceExternalId
}
