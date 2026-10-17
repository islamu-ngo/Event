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
        ArgumentNullException.ThrowIfNull(location);
        return CreateLocationErased(messageId, intent, location.Id, location.ConcurrencyStamp, createdAtUtc);
    }

    public static OutboxMessage CreateLocationErased(
        Guid messageId, PrivacyErasureIntent intent, Guid locationId, Guid locationVersion, DateTime createdAtUtc)
    {
        Validate(messageId, intent, createdAtUtc);
        return Create(
            messageId,
            nameof(Location),
            locationId,
            LocationPiiErasedEventType,
            new LocationPiiErasedPayload(
                1,
                intent.IntentId,
                intent.AuthoritySequence,
                locationId,
                locationVersion),
            createdAtUtc);
    }

    public static OutboxMessage CreateCorrectionRequested(
        Guid messageId,
        PrivacyErasureIntent intent,
        EventLocation eventLocation,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        return CreateCorrectionRequested(messageId, intent, eventLocation.Id, eventLocation.TenantId,
            eventLocation.EventId, eventLocation.LocationId, eventLocation.PolicyVersion, createdAtUtc);
    }

    public static OutboxMessage CreateCorrectionRequested(
        Guid messageId, PrivacyErasureIntent intent, Guid eventLocationId, Guid tenantId,
        Guid eventId, Guid? locationId, int policyVersion, DateTime createdAtUtc)
    {
        Validate(messageId, intent, createdAtUtc);
        return Create(
            messageId,
            nameof(EventLocation),
            eventLocationId,
            LocationPrivacyCorrectionRequestedEventType,
            new LocationPrivacyCorrectionPayload(
                1,
                intent.IntentId,
                intent.AuthoritySequence,
                tenantId,
                eventId,
                eventLocationId,
                locationId,
                policyVersion),
            createdAtUtc);
    }

    public static OutboxMessage CreateProjectionCorrection(
        Guid messageId,
        EventLocation eventLocation,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        return CreateProjectionCorrection(messageId, eventLocation.Id, eventLocation.TenantId,
            eventLocation.EventId, eventLocation.PolicyVersion, createdAtUtc);
    }

    public static OutboxMessage CreateProjectionCorrection(
        Guid messageId, Guid eventLocationId, Guid tenantId, Guid eventId, int policyVersion, DateTime createdAtUtc)
    {
        Validate(messageId, createdAtUtc);
        return Create(
            messageId,
            nameof(EventLocation),
            eventLocationId,
            ProjectionCorrectionEventType,
            new ProjectionCorrectionPayload(
                1,
                tenantId,
                eventId,
                eventLocationId,
                policyVersion),
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
