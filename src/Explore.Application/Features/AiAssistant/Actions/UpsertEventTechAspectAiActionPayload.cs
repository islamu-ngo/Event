namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class UpsertEventTechAspectAiActionPayload
{
    public Guid? EventId { get; init; }

    public Guid? ExpectedConcurrencyStamp { get; init; }

    public string? AspectKind { get; init; }

    public bool? ManagementContextHasEdit { get; init; }

    public string? GithubRepoUrl { get; init; }

    public string? HackathonTrack { get; init; }

    public int? SkillLevel { get; init; }

    public string? TechStackTags { get; init; }

    public bool RequiresLaptop { get; init; }

    public bool IsCodingCompetition { get; init; }

    public int? MaxTeamSize { get; init; }

    public decimal? PrizePool { get; init; }

    public string? PrizeCurrencyCode { get; init; }
}
