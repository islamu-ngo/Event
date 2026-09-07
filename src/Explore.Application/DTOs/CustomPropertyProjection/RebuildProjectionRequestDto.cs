namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record RebuildProjectionRequestDto
{
    public Guid TenantId { get; init; }
    public int? BatchSize { get; init; }
}
