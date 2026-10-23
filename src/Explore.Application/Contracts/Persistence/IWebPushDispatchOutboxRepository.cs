using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebPushDispatchOutboxRepository
{
    Task<WebPushDispatchOutbox> Create(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default);

    Task<bool> CreateIfNotExistsAsync(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default);

    Task<WebPushDispatchOutbox?> GetActiveClaimAsync(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacquires the original live claim and its same-tenant, same-recipient subscription
    /// with user, privacy fence, soft-delete and subscription expiry eligibility in one read.
    /// </summary>
    Task<WebPushDispatchOutbox?> GetSenderEntryClaimAsync(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        DateTime processingStartedAfter,
        DateTime now,
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
