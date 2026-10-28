using Explore.Domain;
using ISLAMU.Moderation.Application;

namespace Explore.Application.Models.InternalEvents;

/// <summary>
/// Wakes the native consequence owner using the exact preallocated effect identity.
/// The durable intent owns revisions and qualification; the message contains no custody evidence.
/// </summary>
public static class ModerationEffectOutboxMessageFactory
{
    public const string AggregateType = nameof(ModerationEffectIntent);
    public const string EventType = "ModerationEffectPlanningRequested";
    public const int MaxRetries = 5;

    public static OutboxMessage Create(ModerationPlannedEffect effect, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return Create(effect.Id, occurredAt);
    }

    public static OutboxMessage Create(Guid effectId, DateTimeOffset occurredAt)
    {
        if (effectId == Guid.Empty || effectId.Version != 7 || effectId.Variant is < 8 or > 11)
            throw new ArgumentException("A durable moderation handoff requires its stable UUIDv7 effect identity.", nameof(effectId));

        return new OutboxMessage
        {
            Id = effectId,
            AggregateType = AggregateType,
            AggregateId = effectId,
            EventType = EventType,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = occurredAt.UtcDateTime,
            MaxRetries = MaxRetries
        };
    }
}
