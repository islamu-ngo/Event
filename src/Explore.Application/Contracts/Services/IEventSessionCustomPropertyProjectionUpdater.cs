namespace Explore.Application.Contracts.Services;

public interface IEventSessionCustomPropertyProjectionUpdater
{
    const string ProjectionName = "event_session_custom_property_projection";
    const int ProjectionVersion = 1;

    Task UpdateForValueAsync(Guid valueId, CancellationToken cancellationToken);

    Task UpdateForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);

    Task RemoveForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);

    Task RefreshForEventSessionAsync(Guid eventSessionId, CancellationToken cancellationToken);

    Task<ProjectionRebuildResult> RebuildForTenantAsync(
        Guid tenantId,
        int? batchSize,
        CancellationToken cancellationToken);

    Task<int> DrainDirtyScopesForTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
