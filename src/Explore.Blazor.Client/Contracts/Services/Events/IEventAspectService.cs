// Provides application-layer abstraction over generated API client methods.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

/// <summary>
/// Service interface for managing Event Aspects (Islamic and Tech characteristics).
/// Wraps generated event-client methods with application-specific error handling.
/// </summary>
public interface IEventAspectService
{
    /// <summary>
    /// Gets the Islamic aspect for an event, if it exists.
    /// </summary>
    Task<EventIslamicAspectDto?> GetIslamicAspectAsync(Guid eventId, bool includeManaged = false);

    /// <summary>
    /// Gets the Tech aspect for an event, if it exists.
    /// </summary>
    Task<EventTechAspectDto?> GetTechAspectAsync(Guid eventId, bool includeManaged = false);

    /// <summary>
    /// Creates the Islamic aspect for an event.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> CreateIslamicAspectAsync(Guid eventId, CreateUpdateIslamicAspectDto dto);

    /// <summary>
    /// Updates the Islamic aspect for an event.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> UpdateIslamicAspectAsync(Guid eventId, UpdateEventIslamicAspectDto dto);

    /// <summary>
    /// Creates the Tech aspect for an event.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> CreateTechAspectAsync(Guid eventId, CreateUpdateTechAspectDto dto);

    /// <summary>
    /// Updates the Tech aspect for an event.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> UpdateTechAspectAsync(Guid eventId, UpdateEventTechAspectDto dto);

    /// <summary>
    /// Deletes the Islamic aspect from an event.
    /// </summary>
    Task<bool> DeleteIslamicAspectAsync(Guid eventId);

    /// <summary>
    /// Deletes the Tech aspect from an event.
    /// </summary>
    Task<bool> DeleteTechAspectAsync(Guid eventId);
}
