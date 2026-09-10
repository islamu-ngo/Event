using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Notifications;

public sealed class NotificationFanoutOccurrencePointerTests
{
    [Test]
    public async Task Serialize_RoundTripsOnlyDurableIdentifiers()
    {
        var pointer = new NotificationFanoutOccurrenceRequested(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            NotificationFanoutOccurrenceRequested.CurrentVersion);

        string json = NotificationFanoutOccurrenceOutboxMessageFactory.SerializePointer(pointer);
        var roundTrip = NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(json);

        await Assert.That(roundTrip).IsEqualTo(pointer);
        using var document = JsonDocument.Parse(json);
        string[] propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(propertyNames).IsEquivalentTo(["tenantId", "occurrenceId", "version"]);
        await Assert.That(json).DoesNotContain("recipient", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("email", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("body", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("title", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("location", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("evidence", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task Create_PreservesCallerOwnedReplayIdAndVersionedEnvelopeFacts()
    {
        DateTime occurredAt = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
        Guid eventId = Guid.CreateVersion7();
        NotificationFanoutOccurrence occurrence = NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            eventId,
            sessionId: null,
            occurredAt,
            audienceCutoffAt: occurredAt,
            Guid.CreateVersion7(),
            "{\"fields\":[\"startTime\"]}",
            "{\"startsAt\":\"2026-08-24T10:00:00Z\"}",
            "{\"startsAt\":\"2026-08-24T11:00:00Z\"}",
            "event.updated",
            templateVersion: 1,
            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            policyVersion: 1,
            priority: 30,
            notBefore: occurredAt,
            sourceType: "event",
            sourceId: eventId,
            coalescingKey: $"event:{eventId:N}:schedule",
            coalescingWindowEndsAt: occurredAt,
            emailDeliveryPolicyRevision: 0);
        Guid messageId = Guid.CreateVersion7();

        OutboxMessage message = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence, messageId);
        NotificationFanoutOccurrenceRequested pointer =
            NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(message.Payload!);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType).IsEqualTo(NotificationFanoutOccurrenceOutboxMessageFactory.EventType);
        await Assert.That(message.AggregateId).IsEqualTo(occurrence.Id);
        await Assert.That(pointer.TenantId).IsEqualTo(occurrence.TenantId);
        await Assert.That(pointer.OccurrenceId).IsEqualTo(occurrence.Id);
        await Assert.That(pointer.Version).IsEqualTo(NotificationFanoutOccurrenceRequested.CurrentVersion);
    }

    [Test]
    public async Task Deserialize_UnknownMember_Throws()
    {
        string json = $$"""
            {
              "tenantId": "{{Guid.CreateVersion7()}}",
              "occurrenceId": "{{Guid.CreateVersion7()}}",
              "version": 1,
              "recipientEmail": "recipient@example.test"
            }
            """;

        await Assert.That(() => NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(json))
            .Throws<JsonException>();
    }
}
