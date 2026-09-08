using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record ProjectionDirtyScopeDto
{
    public long Id { get; init; }
    public string ProjectionName { get; init; } = string.Empty;
    public int ProjectionVersion { get; init; }
    public Guid TenantId { get; init; }
    public CustomPropertyProjectionScopeType ScopeType { get; init; }
    public Guid ScopeId { get; init; }
    public Guid? DefinitionId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DrainedAt { get; init; }
}
