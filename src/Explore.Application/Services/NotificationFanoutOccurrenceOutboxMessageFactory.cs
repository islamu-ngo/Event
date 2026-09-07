using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Serialization;
using Explore.Domain;

namespace Explore.Application.Services;

public static class NotificationFanoutOccurrenceOutboxMessageFactory
{
    public const string EventType = "NotificationFanoutOccurrenceRequested";

    public static OutboxMessage Create(NotificationFanoutOccurrence occurrence) =>
        Create(occurrence, Guid.CreateVersion7());

    public static OutboxMessage Create(NotificationFanoutOccurrence occurrence, Guid outboxMessageId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (outboxMessageId == Guid.Empty)
        {
            throw new ArgumentException("The fanout pointer outbox identifier must be non-empty.", nameof(outboxMessageId));
        }

        var pointer = new NotificationFanoutOccurrenceRequested(
            occurrence.TenantId,
            occurrence.Id,
            NotificationFanoutOccurrenceRequested.CurrentVersion);

        return new OutboxMessage
        {
            Id = outboxMessageId,
            AggregateType = nameof(NotificationFanoutOccurrence),
            AggregateId = occurrence.Id,
            EventType = EventType,
            Payload = SerializePointer(pointer),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = occurrence.OccurredAt,
            MaxRetries = 5,
        };
    }

    public static string SerializePointer(NotificationFanoutOccurrenceRequested pointer)
        => JsonSerializer.Serialize(pointer, ExploreJsonContext.Default.NotificationFanoutOccurrenceRequested);

    public static NotificationFanoutOccurrenceRequested DeserializePointer(string json)
    {
        var pointer = JsonSerializer.Deserialize(
            json,
            ExploreJsonContext.Default.NotificationFanoutOccurrenceRequested)
            ?? throw new JsonException("Fanout occurrence pointer is required.");

        if (pointer.Version != NotificationFanoutOccurrenceRequested.CurrentVersion
            || pointer.TenantId == Guid.Empty
            || pointer.OccurrenceId == Guid.Empty)
        {
            throw new JsonException("Fanout occurrence pointer is invalid.");
        }

        return pointer;
    }
}
