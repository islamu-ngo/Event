using System.Text.Json;
using Explore.Domain;

namespace Explore.Application.Services;

public static class LocationPrivacyOutboxMessageFactory
{
    public const string LocationPiiErasedEventType = "LocationPiiErased";
    public const string LocationPrivacyCorrectionRequestedEventType = "LocationPrivacyCorrectionRequested";
    public const string ProjectionCorrectionEventType = "location.privacy.corrected";

    public static OutboxMessage CreateLocationErased(
        Guid messageId,
        PrivacyErasureIntent intent,
        Location location,
        DateTime createdAtUtc)
    {
        Validate(messageId, intent, createdAtUtc);
        ArgumentNullException.ThrowIfNull(location);
        return Create(
            messageId,
            nameof(Location),
            location.Id,
            LocationPiiErasedEventType,
            new LocationPiiErasedPayload(
                1,
                intent.IntentId,
                intent.AuthoritySequence,
                location.Id,
                location.ConcurrencyStamp),
            createdAtUtc);
    }

    public static OutboxMessage CreateCorrectionRequested(
        Guid messageId,
        PrivacyErasureIntent intent,
        EventLocation eventLocation,
        DateTime createdAtUtc)
    {
        Validate(messageId, intent, createdAtUtc);
        ArgumentNullException.ThrowIfNull(eventLocation);
        return Create(
            messageId,
            nameof(EventLocation),
            eventLocation.Id,
            LocationPrivacyCorrectionRequestedEventType,
            new LocationPrivacyCorrectionPayload(
                1,
                intent.IntentId,
                intent.AuthoritySequence,
                eventLocation.TenantId,
                eventLocation.EventId,
                eventLocation.Id,
                eventLocation.LocationId,
                eventLocation.PolicyVersion),
            createdAtUtc);
    }

    public static OutboxMessage CreateProjectionCorrection(
        Guid messageId,
        EventLocation eventLocation,
        DateTime createdAtUtc)
    {
        Validate(messageId, createdAtUtc);
        ArgumentNullException.ThrowIfNull(eventLocation);
        return Create(
            messageId,
            nameof(EventLocation),
            eventLocation.Id,
            ProjectionCorrectionEventType,
            new ProjectionCorrectionPayload(
                1,
                eventLocation.TenantId,
                eventLocation.EventId,
                eventLocation.Id,
                eventLocation.PolicyVersion),
            createdAtUtc);
    }

    private static OutboxMessage Create<TPayload>(
        Guid messageId,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        TPayload payload,
        DateTime createdAtUtc) => new()
        {
            Id = messageId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAtUtc,
            MaxRetries = 10
        };

    private static void Validate(
        Guid messageId,
        PrivacyErasureIntent intent,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Validate(messageId, createdAtUtc);
    }

    private static void Validate(Guid messageId, DateTime createdAtUtc)
    {
        if (messageId == Guid.Empty || messageId.Version != 7 || messageId.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Outbox message ids must be RFC 4122 UUIDv7 values.", nameof(messageId));
        }

        if (createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Outbox creation time must be non-default UTC.", nameof(createdAtUtc));
        }
    }

    private sealed record LocationPiiErasedPayload(
        int SchemaVersion,
        Guid IntentId,
        long AuthoritySequence,
        Guid LocationId,
        Guid LocationVersion);

    private sealed record LocationPrivacyCorrectionPayload(
        int SchemaVersion,
        Guid IntentId,
        long AuthoritySequence,
        Guid TenantId,
        Guid EventId,
        Guid EventLocationId,
        Guid? LocationId,
        int PolicyVersion);

    private sealed record ProjectionCorrectionPayload(
        int SchemaVersion,
        Guid TenantId,
        Guid EventId,
        Guid EventLocationId,
        int PolicyVersion);
}
