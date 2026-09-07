using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebPushDispatchOutboxRepository
{
    Task<WebPushDispatchOutbox> Create(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default);

    Task<bool> CreateIfNotExistsAsync(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebPushDispatchOutbox>> GetPendingBatch(
        int batchSize,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<int> CountDueDispatchAsync(DateTime now, CancellationToken cancellationToken = default);

    Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken = default);

    Task<int> CountStaleProcessingAsync(DateTime processingStartedBefore, CancellationToken cancellationToken = default);

    Task<int> CountTerminalFailureAsync(CancellationToken cancellationToken = default);

    Task<bool> TryMarkAsProcessing(
        Guid id,
        Guid leaseToken,
        DateTime startedAt,
        CancellationToken cancellationToken = default);

    Task<WebPushDispatchOutbox?> GetActiveClaimAsync(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsDelivered(Guid id, Guid leaseToken, DateTime deliveredAt, CancellationToken cancellationToken = default);

    Task<bool> MarkAsFailed(
        Guid id,
        Guid leaseToken,
        string failureCategory,
        string errorMessage,
        bool isRetryable,
        TimeSpan retryDelay,
        int maxAttempts,
        DateTime failedAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsSkipped(
        Guid id,
        Guid leaseToken,
        string reasonCategory,
        string reasonMessage,
        DateTime skippedAt,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleProcessing(
        DateTime processingStartedBefore,
        DateTime recoveredAt,
        string failureCategory,
        string errorMessage,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> MarkPermanentFailureAndDeactivateSubscription(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        Guid subscriptionId,
        string failureCategory,
        string errorMessage,
        DateTime failedAt,
        CancellationToken cancellationToken = default);
}
