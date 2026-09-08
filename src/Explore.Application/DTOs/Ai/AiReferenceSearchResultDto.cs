namespace Explore.Application.DTOs.Ai;

public sealed record AiReferenceSearchResultDto(
    string Kind,
    Guid ReferenceId,
    string DisplayName,
    string? Summary,
    DateOnly? FirstSessionDate,
    DateOnly? LastSessionDate,
    string? EventStatus,
    string? Visibility,
    string? Format);
