namespace Explore.Application.DTOs.Appearance;

public sealed record SetActiveProfileRequestDto
{
    public Guid ProfileId { get; init; }
    public string? ThemeMode { get; init; }
    public string? Direction { get; init; }
    public string? Language { get; init; }
}
