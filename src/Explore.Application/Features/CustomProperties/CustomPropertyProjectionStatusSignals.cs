using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Domain.Enums;

namespace Explore.Application.Features.CustomProperties;

internal static class CustomPropertyProjectionStatusSignals
{
    private static readonly TimeSpan RebuildStaleAfter = TimeSpan.FromMinutes(10);

    public static void Apply(ProjectionStatusDto dto, int pendingDirtyScopeCount, DateTimeOffset now)
    {
        dto.PendingDirtyScopeCount = pendingDirtyScopeCount;

        if (dto.State == CustomPropertyProjectionState.Failed)
        {
            dto.RequiresOperatorAction = true;
            dto.OperationalState = "failed";
            dto.RecommendedAction = "Inspect LastErrorMessage, then run a tenant projection rebuild after correcting the root cause.";
            return;
        }

        if (dto.State == CustomPropertyProjectionState.Rebuilding)
        {
            var startedAt = dto.LastRebuildStartedAt;
            var isStale = startedAt.HasValue && now - startedAt.Value > RebuildStaleAfter;

            dto.RequiresOperatorAction = isStale;
            dto.OperationalState = isStale ? "rebuild_stale" : "rebuilding";
            dto.RecommendedAction = isStale
                ? "Investigate PostgreSQL advisory-lock waits and the rebuild worker before starting another rebuild."
                : "Monitor the rebuild; dirty scopes will be drained when the rebuild completes.";
            return;
        }

        if (pendingDirtyScopeCount > 0)
        {
            dto.RequiresOperatorAction = true;
            dto.OperationalState = "dirty_backlog_pending";
            dto.RecommendedAction = "Drain dirty scopes for this tenant or run a tenant projection rebuild.";
            return;
        }

        dto.RequiresOperatorAction = false;
        dto.OperationalState = "healthy";
        dto.RecommendedAction = "No custom-property projection operator action is required.";
    }
}
