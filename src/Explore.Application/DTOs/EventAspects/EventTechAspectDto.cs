namespace Explore.Application.DTOs.EventAspects;

using Explore.Application.Models.Common;
using Explore.Domain;

/// <summary>
/// DTO representing the Tech aspect of an event.
/// </summary>
public sealed record EventTechAspectDto
{
    public string? GithubRepoUrl { get; init; }
    public string? HackathonTrack { get; init; }
    public SkillLevel SkillLevel { get; init; }
    public string SkillLevelName => SkillLevel.ToString();
    public string? TechStackTags { get; init; }
    public bool RequiresLaptop { get; init; }
    public bool IsCodingCompetition { get; init; }
    public int? MaxTeamSize { get; init; }
    public decimal? PrizePool { get; init; }
    public string? PrizeCurrencyCode { get; init; }
}

/// <summary>
/// DTO for creating or updating the Tech aspect of an event.
/// </summary>
public sealed record CreateUpdateTechAspectDto
{
    public string? GithubRepoUrl { get; init; }
    public string? HackathonTrack { get; init; }
    public SkillLevel SkillLevel { get; init; } = SkillLevel.AllLevels;
    public string? TechStackTags { get; init; }
    public bool RequiresLaptop { get; init; }
    public bool IsCodingCompetition { get; init; }
    public int? MaxTeamSize { get; init; }
    public decimal? PrizePool { get; init; }
    public string? PrizeCurrencyCode { get; init; }
}

public sealed record UpdateEventTechAspectDto
{
    public UpdateEventTechRepositoryDto? Repository { get; init; }
    public UpdateEventTechClassificationDto? Classification { get; init; }
    public UpdateEventTechParticipationDto? Participation { get; init; }
    public UpdateEventTechPrizeDto? Prize { get; init; }
}

public sealed record UpdateEventTechRepositoryDto
{
    public OptionalUpdate<string?> GithubRepoUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventTechClassificationDto
{
    public OptionalUpdate<string?> HackathonTrack { get; init; } = OptionalUpdate<string?>.Unspecified();
    public SkillLevel? SkillLevel { get; init; }
    public OptionalUpdate<string?> TechStackTags { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventTechParticipationDto
{
    public bool? RequiresLaptop { get; init; }
    public bool? IsCodingCompetition { get; init; }
}

public sealed record UpdateEventTechPrizeDto
{
    public OptionalUpdate<int?> MaxTeamSize { get; init; } = OptionalUpdate<int?>.Unspecified();
    public OptionalUpdate<decimal?> PrizePool { get; init; } = OptionalUpdate<decimal?>.Unspecified();
    public OptionalUpdate<string?> PrizeCurrencyCode { get; init; } = OptionalUpdate<string?>.Unspecified();
}
