namespace Explore.Application.Contracts.Persistence;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Explore.Domain;

/// <summary>
/// Repository interface for EventIslamicAspect entity operations.
/// Uses shared primary key pattern where Id = Event.Id.
/// </summary>
public interface IEventIslamicAspectRepository : IGenericRepository<EventIslamicAspect, Guid>
{
    /// <summary>
    /// Gets the Islamic aspect for an event with all navigation properties loaded.
    /// </summary>
    /// <param name="eventId">The event ID (also the aspect ID due to shared PK).</param>
    /// <returns>The Islamic aspect with Madhab and PrimaryLanguage loaded, or null if not found.</returns>
    Task<EventIslamicAspect?> GetByEventIdWithDetails(Guid eventId);

    /// <summary>
    /// Gets all Islamic aspects for events by gender mode.
    /// </summary>
    /// <param name="genderMode">The gender segregation mode to filter by.</param>
    /// <returns>List of Islamic aspects matching the gender mode.</returns>
    Task<IReadOnlyList<EventIslamicAspect>> GetByGenderMode(GenderSegregationMode genderMode);

    /// <summary>
    /// Gets all Islamic aspects for events by Madhab.
    /// </summary>
    /// <param name="madhabId">The Madhab ID to filter by.</param>
    /// <returns>List of Islamic aspects for the specified Madhab.</returns>
    Task<IReadOnlyList<EventIslamicAspect>> GetByMadhab(int madhabId);

    /// <summary>
    /// Creates or updates the Islamic aspect for an event.
    /// </summary>
    /// <param name="aspect">The aspect to upsert.</param>
    /// <returns>The created or updated aspect.</returns>
    Task<EventIslamicAspect> Upsert(EventIslamicAspect aspect);
}
