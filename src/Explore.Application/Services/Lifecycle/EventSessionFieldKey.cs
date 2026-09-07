namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Stable product-concept field keys used in session readiness and validation errors.
/// These map to user-facing or API-contract field paths, not internal CLR property names.
/// </summary>
public enum EventSessionFieldKey
{
    /// <summary>Session title / label.</summary>
    Title,

    /// <summary>Session description / abstract.</summary>
    Description,

    /// <summary>Parent event reference.</summary>
    ParentEvent,

    /// <summary>Tenant scope reference.</summary>
    Tenant,

    /// <summary>Current lifecycle status lookup.</summary>
    Status,

    /// <summary>Schedule start UTC instant.</summary>
    ScheduleStart,

    /// <summary>Schedule end UTC instant.</summary>
    ScheduleEnd,

    /// <summary>Room assignment within a location.</summary>
    Room,

    /// <summary>Physical location reference.</summary>
    Location,

    /// <summary>Event day assignment.</summary>
    Day,

    /// <summary>Session kind lookup (talk/workshop/panel/etc).</summary>
    Kind,

    /// <summary>Registration mode lookup.</summary>
    RegistrationMode,

    /// <summary>At least one speaker is assigned.</summary>
    Speakers,

    /// <summary>Parent event is in a compatible published state.</summary>
    ParentEventCompatibility
}
