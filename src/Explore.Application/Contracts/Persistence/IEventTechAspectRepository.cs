namespace Explore.Application.Contracts.Persistence;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Explore.Domain;

/// <summary>
/// Repository interface for EventTechAspect entity operations.
/// Uses shared primary key pattern where Id = Event.Id.
/// </summary>
public interface IEventTechAspectRepository : IGenericRepository<EventTechAspect, Guid>
{
    /// <summary>
    /// Gets the Tech aspect for an event.
    /// </summary>
    /// <param name="eventId">The event ID (also the aspect ID due to shared PK).</param>
    /// <returns>The Tech aspect or null if not found.</returns>
    Task<EventTechAspect?> GetByEventId(Guid eventId);

    /// <summary>
    /// Gets all Tech aspects by skill level.
    /// </summary>
    /// <param name="skillLevel">The skill level to filter by.</param>
    /// <returns>List of Tech aspects matching the skill level.</returns>
    Task<IReadOnlyList<EventTechAspect>> GetBySkillLevel(SkillLevel skillLevel);

    /// <summary>
    /// Gets all Tech aspects for coding competitions.
    /// </summary>
    /// <returns>List of Tech aspects where IsCodingCompetition is true.</returns>
    Task<IReadOnlyList<EventTechAspect>> GetCodingCompetitions();

    /// <summary>
    /// Gets all Tech aspects with hackathon tracks.
    /// </summary>
    /// <returns>List of Tech aspects that have hackathon tracks defined.</returns>
    Task<IReadOnlyList<EventTechAspect>> GetHackathons();

    /// <summary>
    /// Creates or updates the Tech aspect for an event.
    /// </summary>
    /// <param name="aspect">The aspect to upsert.</param>
    /// <returns>The created or updated aspect.</returns>
    Task<EventTechAspect> Upsert(EventTechAspect aspect);
}
