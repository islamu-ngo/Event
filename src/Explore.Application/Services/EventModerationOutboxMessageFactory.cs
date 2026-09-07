using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;

namespace Explore.Application.Services;

public static class EventModerationOutboxMessageFactory
{
    private const string EventAggregateType = "Event";
    public const string EventLightModeratedNotificationFanoutRequestedEventType = "EventLightModeratedNotificationFanoutRequested";
    public const string EventHeavyRedactedNotificationFanoutRequestedEventType = "EventHeavyRedactedNotificationFanoutRequested";

    public static OutboxMessage CreateLightModerationNotificationFanoutMessage(
        Guid id,
        Event @event,
        EventModerationRecord moderationRecord)
    {
        var payload = new EventLightModeratedNotificationFanoutRequested
        {
            TenantId = @event.TenantId,
            EventId = @event.Id,
            ModerationRecordId = moderationRecord.Id,
            EventTitle = @event.Title,
            SourceActorId = @event.ActorId,
            ModeratedAt = moderationRecord.CreatedAt
        };

        return new OutboxMessage
        {
            Id = id,
            AggregateType = EventAggregateType,
            AggregateId = @event.Id,
            EventType = EventLightModeratedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = moderationRecord.CreatedAt.UtcDateTime,
            MaxRetries = 5
        };
    }

    public static OutboxMessage CreateHeavyRedactionNotificationFanoutMessage(
        Event @event,
        EventModerationRecord moderationRecord)
    {
        var payload = new EventHeavyRedactedNotificationFanoutRequested
        {
            TenantId = @event.TenantId,
            ModerationRecordId = moderationRecord.Id,
            Version = EventHeavyRedactedNotificationFanoutRequested.CurrentVersion
        };

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = EventAggregateType,
            AggregateId = @event.Id,
            EventType = EventHeavyRedactedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = moderationRecord.CreatedAt.UtcDateTime,
            MaxRetries = 5
        };
    }
}
