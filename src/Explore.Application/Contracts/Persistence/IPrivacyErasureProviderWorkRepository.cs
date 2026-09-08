using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPrivacyErasureProviderWorkRepository
{
    Task<int> AddMissingAsync(
        IReadOnlyCollection<PrivacyErasureProviderWork> work,
        CancellationToken cancellationToken);

    Task<int> ExpireLocatorsAsync(
        DateTime utcNow,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivacyErasureProviderWork>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryMarkSucceededAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime completedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryMarkUnknownAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime unknownAtUtc,
        string failureCode,
        CancellationToken cancellationToken);

    Task<bool> TryReconcileUnknownAsync(
        Guid id,
        long fenceToken,
        PrivacyErasureProviderReconciliation outcome,
        DateTime reconciledAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryScheduleRetryAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime failedAtUtc,
        DateTime nextAttemptAtUtc,
        string failureCode,
        CancellationToken cancellationToken);

    Task<bool> TryDeadLetterAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken);

    Task<int> CountOutstandingAsync(Guid intentId, CancellationToken cancellationToken);
    Task<int> CountCompletedAsync(Guid intentId, CancellationToken cancellationToken);
    Task<int> CountUnknownAsync(CancellationToken cancellationToken);
    Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken);
    Task<int> CountDueAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<int> CleanupCompletedAsync(DateTime cutoffUtc, int batchSize, bool dryRun, CancellationToken cancellationToken);
}
