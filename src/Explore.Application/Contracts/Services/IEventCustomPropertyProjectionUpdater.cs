namespace Explore.Application.Contracts.Services;

public interface IEventCustomPropertyProjectionUpdater
{
    const string ProjectionName = "event_custom_property_projection";
    const int ProjectionVersion = 1;

    Task UpdateForValueAsync(Guid valueId, CancellationToken cancellationToken);

    Task UpdateForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);

    Task RemoveForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);

    Task RefreshForEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<ProjectionRebuildResult> RebuildForTenantAsync(
        Guid tenantId,
        int? batchSize,
        CancellationToken cancellationToken);

    Task<int> DrainDirtyScopesForTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record ProjectionRebuildResult(
    bool LockAcquired,
    long RowsProcessed,
    long RowsFailed,
    int DrainedDirtyScopes);
