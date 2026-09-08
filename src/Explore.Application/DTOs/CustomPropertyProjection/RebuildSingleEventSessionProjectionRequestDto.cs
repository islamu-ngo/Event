namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record RebuildSingleEventSessionProjectionRequestDto
{
    public Guid EventSessionId { get; init; }
}
