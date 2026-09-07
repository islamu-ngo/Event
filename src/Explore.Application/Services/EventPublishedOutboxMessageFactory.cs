using System;
using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;

namespace Explore.Application.Services;

public static class EventPublishedOutboxMessageFactory
{
    private const string EventAggregateType = "Event";
    public const string EventPublishedNotificationFanoutRequestedEventType = "EventPublishedNotificationFanoutRequested";

    public static OutboxMessage CreateNotificationFanoutOutboxMessage(
        Guid outboxMessageId,
        Event @event,
        DateTimeOffset publishedAt)
    {
        var payload = new EventPublishedNotificationFanoutRequested
        {
            TenantId = @event.TenantId,
            EventId = @event.Id,
            EventTitle = @event.Title,
            SourceActorId = @event.ActorId,
            StartDate = @event.FirstSessionStartUtc ?? publishedAt,
            EndDate = @event.LastSessionStartUtc,
            PublishedAt = publishedAt
        };

        return new OutboxMessage
        {
            Id = outboxMessageId,
            AggregateType = EventAggregateType,
            AggregateId = @event.Id,
            EventType = EventPublishedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = publishedAt.UtcDateTime,
            MaxRetries = 5
        };
    }
}
