namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
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
        DateTime occurredAt,
        ImmutableArray<SettingChangedNotification> deferredNotifications = default)
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
            Payload = deferredNotifications.IsDefaultOrEmpty
                ? null
                : JsonSerializer.Serialize(deferredNotifications),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = occurredAt,
            MaxRetries = 5
        };
    }

    public static ImmutableArray<SettingChangedNotification> ReadNotifications(
        string? payload) => string.IsNullOrEmpty(payload)
        ? []
        : [.. JsonSerializer.Deserialize<SettingChangedNotification[]>(payload)
            ?? throw new JsonException(
                "Configuration import effect notifications are invalid.")];
}
