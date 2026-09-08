using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PaymentSucceededObservation : ITenantEntity, IAuditableEntity
{
    private PaymentSucceededObservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid PaymentAttemptId { get; private set; }
    public Guid? SourceIncomingWebhookMessageId { get; private set; }
    public string ProviderCheckoutSessionId { get; private set; } = string.Empty;
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public string? ProviderRequestId { get; private set; }
    public DateTime ObservedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static PaymentSucceededObservation Create(
        PaymentAttempt attempt,
        Guid? sourceIncomingWebhookMessageId,
        string checkoutSessionId,
        string paymentId,
        string? providerRequestId,
        DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (observedAt == default || observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Observation timestamp must be non-default UTC.", nameof(observedAt));
        }

        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = attempt.TenantId,
            RegistrationOrderId = attempt.RegistrationOrderId,
            PaymentAttemptId = attempt.Id,
            SourceIncomingWebhookMessageId = sourceIncomingWebhookMessageId,
            ProviderCheckoutSessionId = Require(checkoutSessionId, 200),
            ProviderPaymentId = Require(paymentId, 200),
            ProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId) ? null : Require(providerRequestId, 120),
            ObservedAt = observedAt,
            CreatedAt = observedAt
        };
    }

    private static string Require(string value, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
