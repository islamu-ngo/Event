namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record DrainDirtyScopesResponseDto
{
    public int DrainedCount { get; init; }
    public DateTimeOffset DrainedAt { get; init; }
}
