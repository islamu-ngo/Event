namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Identifies the validation profile applied to a lifecycle command.
/// Profiles determine which field keys are required, optional, or forbidden
/// for a given command/source/state combination.
/// </summary>
public enum ValidationProfile
{
    /// <summary>
    /// Native progressive draft shell creation.
    /// Minimal: title, tenant, owner Actor, status/defaults.
    /// </summary>
    EventDraftCreate,

    /// <summary>
    /// User/platform-created event submission.
    /// Configurable, often stricter on hosted instances.
    /// </summary>
    EventNativeSubmit,

    /// <summary>
    /// External import/bot/backfill event creation.
    /// Tolerant but provenance required.
    /// </summary>
    EventImportCreate,

    /// <summary>
    /// Historical/past-event archive creation.
    /// Tolerant, not publication-ready by default.
    /// </summary>
    EventArchiveCreate,

    /// <summary>
    /// Public event publication.
    /// Strict, policy-aware, public/export/federation safe.
    /// </summary>
    EventPublish,

    EventPublishCommunityLexicon,

    /// <summary>
    /// Draft/proposal session under an event.
    /// Minimal: parent event, tenant, title or placeholder label, status.
    /// </summary>
    SessionDraftCreate,

    /// <summary>
    /// Assign time/room/day to a session.
    /// Requires valid times and room conflict checks.
    /// </summary>
    SessionSchedule,

    /// <summary>
    /// Public program item publication.
    /// Requires status, title, schedule, visibility, parent event compatibility.
    /// </summary>
    SessionPublish
}
