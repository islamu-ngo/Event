using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record WebhookBulkReplayFilter(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? WebhookConsumerId,
    Guid? WebhookEndpointId,
    string? EventType);

public interface IWebhookBulkReplayRepository
{
    Task<WebhookBulkReplayPreviewSnapshot> PreviewAsync(
        Guid tenantId,
        WebhookBulkReplayFilter filter,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task AcquireTenantScheduleLockAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<WebhookBulkReplayOperation?> GetByOperationKeyAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken);

    Task<WebhookBulkReplayOperation?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookBulkReplayOperation>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken);

    Task<int> CountReservedItemsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<WebhookBulkReplayOperation> CreateAsync(
        WebhookBulkReplayOperation operation,
        CancellationToken cancellationToken);

    Task<WebhookBulkReplayOperation?> GetNextQueuedForUpdateAsync(CancellationToken cancellationToken);

    Task<int> ScheduleEligibleLocalTargetsAsync(
        WebhookBulkReplayOperation operation,
        DateTime scheduledAt,
        CancellationToken cancellationToken);

    Task<WebhookBulkReplayOperation> UpdateAsync(
        WebhookBulkReplayOperation operation,
        CancellationToken cancellationToken);
}
