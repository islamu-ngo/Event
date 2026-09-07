using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IConfigurationManifestEffectOutboxRepository
{
    Task<OutboxMessage> Create(OutboxMessage message);

    Task<OutboxMessage?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxMessage>> GetPendingManifestEffectsAsync(
        int batchSize,
        CancellationToken cancellationToken);

    Task<DateTime?> TryClaimForProcessing(
        Guid id,
        DateTime claimedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkAsCompleted(
        Guid id,
        DateTime processingLeaseExpiresAt,
        CancellationToken cancellationToken);

    Task<OutboxFailureTransition> MarkAsFailed(
        Guid id,
        DateTime processingLeaseExpiresAt,
        string error,
        bool isRetryable,
        int retryDelaySeconds,
        DateTime failedAt,
        CancellationToken cancellationToken);
}
