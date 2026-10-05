namespace Explore.Application.DTOs.Event;

public sealed record EventMatchingSessionDto(
    Guid Id,
    string? Title,
    DateOnly LocalStartDate,
    TimeOnly? LocalStartTime,
    DateOnly? LocalEndDate,
    TimeOnly? LocalEndTime,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    bool IsOpenEnded)
{
    public string? City { get; init; }
    public string? Country { get; init; }
}
