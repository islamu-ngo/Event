using System.Text.Json;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed record RefundCampaignProcessPayload(Guid TenantId, Guid CampaignId);
public sealed record RefundAttemptProcessPayload(Guid TenantId, Guid RefundAttemptId);
public sealed record PaymentCancellationProcessPayload(Guid TenantId, Guid CampaignId, Guid PaymentAttemptId);

public static class RefundOutboxMessageFactory
{
    public const string CampaignProcessRequested = "registration.refund_campaign.process_requested";
    public const string DispatchRequested = "registration.refund.dispatch_requested";
    public const string ReconciliationRequested = "registration.refund.reconciliation_requested";
    public const string PaymentCancellationRequested = "registration.payment.cancellation_requested";

    public static OutboxMessage CreateCampaignProcess(RefundCampaign campaign, DateTime createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(RefundCampaign),
        AggregateId = campaign.Id,
        EventType = CampaignProcessRequested,
        Payload = JsonSerializer.Serialize(new RefundCampaignProcessPayload(campaign.TenantId, campaign.Id)),
        Status = OutboxMessageStatus.Pending,
        CreatedAt = createdAt,
        MaxRetries = 10
    };

    public static OutboxMessage CreateDispatch(RefundAttempt attempt, DateTime createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(RefundAttempt),
        AggregateId = attempt.Id,
        EventType = DispatchRequested,
        Payload = JsonSerializer.Serialize(new RefundAttemptProcessPayload(attempt.TenantId, attempt.Id)),
        Status = OutboxMessageStatus.Pending,
        CreatedAt = createdAt,
        MaxRetries = 10
    };

    public static OutboxMessage CreateReconciliation(RefundAttempt attempt, DateTime createdAt, DateTime dueAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(RefundAttempt),
        AggregateId = attempt.Id,
        EventType = ReconciliationRequested,
        Payload = JsonSerializer.Serialize(new RefundAttemptProcessPayload(attempt.TenantId, attempt.Id)),
        Status = OutboxMessageStatus.Pending,
        CreatedAt = createdAt,
        NextRetryAt = dueAt,
        MaxRetries = 10
    };

    public static OutboxMessage CreatePaymentCancellation(
        RefundCampaign campaign,
        PaymentAttempt payment,
        DateTime createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(PaymentAttempt),
        AggregateId = payment.Id,
        EventType = PaymentCancellationRequested,
        Payload = JsonSerializer.Serialize(new PaymentCancellationProcessPayload(
            campaign.TenantId, campaign.Id, payment.Id)),
        Status = OutboxMessageStatus.Pending,
        CreatedAt = createdAt,
        MaxRetries = 10
    };

    public static RefundCampaignProcessPayload ReadCampaign(OutboxMessage message) =>
        JsonSerializer.Deserialize<RefundCampaignProcessPayload>(message.Payload
            ?? throw new InvalidOperationException("Refund campaign trigger payload is required."))
        ?? throw new InvalidOperationException("Refund campaign trigger payload is invalid.");

    public static RefundAttemptProcessPayload ReadAttempt(OutboxMessage message) =>
        JsonSerializer.Deserialize<RefundAttemptProcessPayload>(message.Payload
            ?? throw new InvalidOperationException("Refund attempt trigger payload is required."))
        ?? throw new InvalidOperationException("Refund attempt trigger payload is invalid.");

    public static PaymentCancellationProcessPayload ReadPaymentCancellation(OutboxMessage message) =>
        JsonSerializer.Deserialize<PaymentCancellationProcessPayload>(message.Payload
            ?? throw new InvalidOperationException("Payment cancellation trigger payload is required."))
        ?? throw new InvalidOperationException("Payment cancellation trigger payload is invalid.");
}
