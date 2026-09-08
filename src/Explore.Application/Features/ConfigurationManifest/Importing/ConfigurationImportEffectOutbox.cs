namespace Explore.Application.Features.ConfigurationManifest.Importing;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;

public interface IConfigurationImportEffectOutboxRepository
{
    Task<OutboxMessage> Create(OutboxMessage message);
    Task<OutboxMessage?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxMessage>> GetPendingImportEffectsAsync(
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

public static class ConfigurationImportEffectOutbox
{
    public const string AggregateType = nameof(ConfigurationImportOperation);
    public const string EventType = "ConfigurationImportEffectsRequested";

    public static OutboxMessage Create(
        Guid messageId,
        Guid operationId,
        DateTime occurredAt)
    {
        if (messageId == Guid.Empty || messageId.Version != 7
            || operationId == Guid.Empty || operationId.Version != 7)
        {
            throw new ArgumentException("Import outbox identities must be UUIDv7.");
        }
        if (occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(occurredAt));
        return new OutboxMessage
        {
            Id = messageId,
            AggregateType = AggregateType,
            AggregateId = operationId,
            EventType = EventType,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = occurredAt,
            MaxRetries = 5
        };
    }
}
