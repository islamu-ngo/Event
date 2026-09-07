using Explore.Domain;

namespace Explore.Application.Services;

public static class PrivacyErasureCacheInvalidationOutboxMessageFactory
{
    public const string EventType = "PrivacyErasureCacheInvalidationRequested";

    public static OutboxMessage Create(Guid messageId, Guid subjectId, DateTime createdAtUtc)
    {
        if (messageId == Guid.Empty || messageId.Version != 7 || messageId.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Outbox message ids must be RFC 4122 UUIDv7 values.", nameof(messageId));
        }

        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("The erased User id is required.", nameof(subjectId));
        }

        if (createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Outbox creation time must be non-default UTC.", nameof(createdAtUtc));
        }

        return new OutboxMessage
        {
            Id = messageId,
            AggregateType = nameof(User),
            AggregateId = subjectId,
            EventType = EventType,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAtUtc,
            MaxRetries = 10
        };
    }
}
