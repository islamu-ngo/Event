using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record ProjectionStatusDto
{
    public string ProjectionName { get; init; } = string.Empty;
    public int ProjectionVersion { get; init; }
    public Guid TenantId { get; init; }
    public CustomPropertyProjectionState State { get; init; }
    public DateTimeOffset? LastRebuildStartedAt { get; init; }
    public DateTimeOffset? LastRebuildCompletedAt { get; init; }
    public long RowsProcessed { get; init; }
    public long RowsFailed { get; init; }
    public string? LastCheckpoint { get; init; }
    public string? LastErrorMessage { get; init; }
    public int PendingDirtyScopeCount { get; set; }
    public bool RequiresOperatorAction { get; set; }
    public string OperationalState { get; set; } = "unknown";
    public string RecommendedAction { get; set; } = "Inspect projection status and dirty-scope backlog.";
}
