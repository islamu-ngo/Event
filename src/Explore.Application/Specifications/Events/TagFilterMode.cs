// Used by GetEventListRequest to control inclusion and exclusion query semantics.

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Defines how multiple tag IDs are combined when filtering events.
/// </summary>
public enum TagFilterMode
{
    /// <summary>
    /// AND mode: all specified tags must match.
    /// For inclusion: event must have ALL included tags.
    /// For exclusion: exclude only if event has ALL excluded tags simultaneously.
    /// </summary>
    And = 0,

    /// <summary>
    /// OR mode: at least one specified tag must match.
    /// For inclusion: event must have at least one included tag.
    /// For exclusion: exclude if event has ANY excluded tag.
    /// </summary>
    Or = 1
}
