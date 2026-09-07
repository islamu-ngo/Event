using System.Text.Json;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed record AdmissionEventCancellationPayload(Guid TenantId, Guid EventId);

public static class AdmissionRevocationOutboxMessageFactory
{
    public const string EventCancellationRequested =
        "admission.event_cancellation.requested";

    public static OutboxMessage CreateEventCancellation(
        Guid tenantId,
        Guid eventId,
        DateTime createdAt)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty ||
            createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Admission event cancellation requires tenant, event, and UTC time.");
        }

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(AdmissionTicket),
            AggregateId = eventId,
            EventType = EventCancellationRequested,
            Payload = JsonSerializer.Serialize(
                new AdmissionEventCancellationPayload(tenantId, eventId)),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt,
            MaxRetries = 10
        };
    }

    public static AdmissionEventCancellationPayload ReadEventCancellation(
        OutboxMessage message) =>
        JsonSerializer.Deserialize<AdmissionEventCancellationPayload>(
            message.Payload ??
            throw new InvalidOperationException("Admission event cancellation payload is required."))
        ?? throw new InvalidOperationException("Admission event cancellation payload is invalid.");
}
