namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record DrainDirtyScopesRequestDto
{
    public Guid TenantId { get; init; }
    public string ProjectionName { get; init; } = string.Empty;
}
