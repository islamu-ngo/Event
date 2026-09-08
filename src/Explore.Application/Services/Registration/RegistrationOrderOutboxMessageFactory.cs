using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public static class RegistrationOrderOutboxMessageFactory
{
    private const string AggregateType = "RegistrationOrder";
    public const string ConfirmedEventType = "RegistrationOrderConfirmed";
    public const string CancelledEventType = "RegistrationOrderCancelled";
    public const string RejectedEventType = "RegistrationOrderRejected";

    public static OutboxMessage Create(
        Guid messageId,
        RegistrationOrder order,
        RegistrationOrderStatusEnum status,
        DateTime createdAt,
        int admissionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (messageId == Guid.Empty || createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Outbox identity and UTC timestamp are required.");
        }

        return new OutboxMessage
        {
            Id = messageId,
            AggregateType = AggregateType,
            AggregateId = order.Id,
            EventType = GetEventType(status),
            Payload = JsonSerializer.Serialize(new RegistrationOrderLifecycleOutboxPayload(
                order.Id,
                order.EventId,
                order.TenantId,
                (int)status,
                admissionCount,
                status == RegistrationOrderStatusEnum.Confirmed)),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt,
            MaxRetries = 5
        };
    }

    private static string GetEventType(RegistrationOrderStatusEnum status) => status switch
    {
        RegistrationOrderStatusEnum.Confirmed => ConfirmedEventType,
        RegistrationOrderStatusEnum.Cancelled => CancelledEventType,
        RegistrationOrderStatusEnum.Rejected => RejectedEventType,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Only terminal lifecycle transitions create general outbox messages.")
    };

    public static RegistrationOrderLifecycleOutboxPayload ReadLifecycle(OutboxMessage message) =>
        JsonSerializer.Deserialize<RegistrationOrderLifecycleOutboxPayload>(message.Payload
            ?? throw new InvalidOperationException("Registration-order lifecycle payload is required."))
        ?? throw new InvalidOperationException("Registration-order lifecycle payload is invalid.");
}

public sealed record RegistrationOrderLifecycleOutboxPayload(
    Guid RegistrationOrderId,
    Guid EventId,
    Guid TenantId,
    int StatusId,
    int AdmissionCount,
    bool AdmissionIssuanceRequested);
