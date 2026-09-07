namespace Explore.Application.DTOs.Ai;

public sealed record AiSelectedReferenceDto(
    string Kind,
    Guid ReferenceId,
    string DisplayName,
    string? Summary);
