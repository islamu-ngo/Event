using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripeRefundIncomingWebhookHandler(IRefundAttemptRepository repository) : IIncomingWebhookHandler
{
    public const string EffectKindValue = "refund-provider-observation";

    public string EffectKind => EffectKindValue;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, StripeConnectIncomingWebhookVerifier.ProviderCode, StringComparison.Ordinal) &&
        StripeConnectIncomingWebhookVerifier.IsRefundEvent(eventType);

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        StripeRefundWebhookEnvelope? envelope;
        try
        {
            envelope = StripeRefundWebhookEnvelope.Deserialize(context.PayloadBytes.Span);
        }
        catch (System.Text.Json.JsonException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_refund_envelope_invalid");
        }

        if (envelope is null || envelope.CreatedAt.Kind != DateTimeKind.Utc ||
            !string.Equals(envelope.EventId, context.ProviderMessageId, StringComparison.Ordinal) ||
            !string.Equals(envelope.EventType, context.EventType, StringComparison.Ordinal))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_refund_envelope_mismatch");
        }

        RefundAttempt? attempt = await repository.GetByIdAsync(context.TenantId, envelope.RefundAttemptId, cancellationToken);
        if (attempt is null ||
            !string.Equals(attempt.ProviderCode, StripeConnectIncomingWebhookVerifier.PaymentProviderCode, StringComparison.Ordinal) ||
            !string.Equals(attempt.ExternalAccountId, envelope.AccountId, StringComparison.Ordinal) ||
            !string.Equals(attempt.ProviderPaymentId, envelope.ProviderPaymentId, StringComparison.Ordinal) ||
            (attempt.ProviderRefundId is not null && !string.Equals(attempt.ProviderRefundId, envelope.ProviderRefundId, StringComparison.Ordinal)))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_refund_attempt_context_mismatch");
        }
        if (envelope.CreatedAt < attempt.LastObservedAt)
        {
            return IncomingWebhookProcessingResult.Processed(attempt.Id.ToString("N"));
        }

        try
        {
            RefundAttemptEvidence.Apply(
                attempt,
                new RefundProviderObservation(
                    envelope.ProviderRefundId,
                    envelope.ProviderPaymentId,
                    envelope.Status,
                    envelope.AmountMinor,
                    envelope.CurrencyCode,
                    null),
                envelope.CreatedAt,
                null);
            await repository.SaveChangesAsync(cancellationToken);
            return IncomingWebhookProcessingResult.Processed(attempt.Id.ToString("N"));
        }
        catch (InvalidOperationException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_refund_evidence_conflict");
        }
    }
}
