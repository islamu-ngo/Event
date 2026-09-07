namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record RebuildSingleEventProjectionRequestDto
{
    public Guid EventId { get; init; }
}
