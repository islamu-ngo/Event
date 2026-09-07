// Uses 1:1 shared primary key pattern where Id is both PK and FK to Event.

namespace Explore.Domain;

/// <summary>
/// Tech-specific aspects for events. Uses shared primary key pattern (Id = Event.Id).
/// Only created when an event has tech/developer characteristics.
/// </summary>
public class EventTechAspect
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
    /// GitHub repository URL for hackathons/workshops.
    /// </summary>
    public string? GithubRepoUrl { get; set; }

    /// <summary>
    /// Hackathon track name (if applicable).
    /// </summary>
    public string? HackathonTrack { get; set; }

    /// <summary>
    /// Required skill level for attendees.
    /// </summary>
    public SkillLevel SkillLevel { get; set; } = SkillLevel.AllLevels;

    /// <summary>
    /// Comma-separated list of tech stack tags (e.g., ".NET, React, PostgreSQL").
    /// </summary>
    public string? TechStackTags { get; set; }

    /// <summary>
    /// Whether laptops/devices are required.
    /// </summary>
    public bool RequiresLaptop { get; set; }

    /// <summary>
    /// Whether the event is a coding competition.
    /// </summary>
    public bool IsCodingCompetition { get; set; }

    /// <summary>
    /// Maximum team size for hackathons (null = individual).
    /// </summary>
    public int? MaxTeamSize { get; set; }

    /// <summary>
    /// Prize pool amount (if applicable).
    /// </summary>
    public decimal? PrizePool { get; set; }

    /// <summary>
    /// Prize pool currency code (e.g., "USD", "EUR").
    /// </summary>
    public string? PrizeCurrencyCode { get; set; }
}

/// <summary>
/// Skill level requirements for tech events.
/// </summary>
public enum SkillLevel
{
    /// <summary>All skill levels welcome.</summary>
    AllLevels = 0,
    /// <summary>Beginner-friendly content.</summary>
    Beginner = 1,
    /// <summary>Intermediate knowledge required.</summary>
    Intermediate = 2,
    /// <summary>Advanced/expert level content.</summary>
    Advanced = 3
}
