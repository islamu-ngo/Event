namespace Explore.Application.Contracts.Persistence;

/// <summary>Bounded Global queue authority exposes ownership, never dispatch or subscription content.</summary>
public interface IWebPushDispatchAdmission
{
    const int MaximumBatchSize = 1000;

    Task<IReadOnlyList<WebPushDispatchOwner>> GetPendingBatch(
        int batchSize, DateTime now, CancellationToken cancellationToken = default);

    Task<bool> TryMarkAsProcessing(
        WebPushDispatchOwner owner, Guid leaseToken, DateTime startedAt,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleProcessing(
        DateTime processingStartedBefore, DateTime recoveredAt, string failureCategory,
        string errorMessage, int batchSize, CancellationToken cancellationToken = default);

    Task<int> CountDueDispatchAsync(DateTime now, CancellationToken cancellationToken = default);
    Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken = default);
    Task<int> CountStaleProcessingAsync(DateTime processingStartedBefore, CancellationToken cancellationToken = default);
    Task<int> CountTerminalFailureAsync(CancellationToken cancellationToken = default);
}

public sealed record WebPushDispatchOwner(Guid TenantId, Guid Id);
