namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record RebuildProjectionResponseDto
{
    public bool LockAcquired { get; init; }
    public long RowsProcessed { get; init; }
    public long RowsFailed { get; init; }
    public int DrainedDirtyScopes { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
