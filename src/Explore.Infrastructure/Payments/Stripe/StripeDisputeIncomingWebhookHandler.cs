using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripeDisputeIncomingWebhookHandler(IRefundAttemptRepository repository) : IIncomingWebhookHandler
{
    public const string EffectKindValue = "payment-dispute-observation";

    public string EffectKind => EffectKindValue;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, StripeConnectIncomingWebhookVerifier.ProviderCode, StringComparison.Ordinal) &&
        StripeConnectIncomingWebhookVerifier.IsDisputeEvent(eventType);

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        StripeDisputeWebhookEnvelope? envelope;
        try
        {
            envelope = StripeDisputeWebhookEnvelope.Deserialize(context.PayloadBytes.Span);
        }
        catch (System.Text.Json.JsonException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_dispute_envelope_invalid");
        }

        if (envelope is null || envelope.CreatedAt.Kind != DateTimeKind.Utc ||
            !string.Equals(envelope.EventId, context.ProviderMessageId, StringComparison.Ordinal) ||
            !string.Equals(envelope.EventType, context.EventType, StringComparison.Ordinal))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_dispute_envelope_mismatch");
        }

        PaymentAttempt? payment = await repository.FindPaymentByProviderPaymentAsync(
            context.TenantId,
            envelope.AccountId,
            envelope.ProviderPaymentId,
            cancellationToken);
        if (payment is null)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_dispute_payment_orphaned");
        }

        PaymentDispute dispute = PaymentDispute.Observe(
            Guid.CreateVersion7(),
            context.TenantId,
            payment.Id,
            envelope.ProviderDisputeId,
            envelope.Stage,
            envelope.Status,
            envelope.AmountMinor,
            envelope.CurrencyCode,
            envelope.CreatedAt,
            envelope.ResponseDueAt);
        PaymentDispute observed = await repository.ObserveDisputeAsync(dispute, cancellationToken);
        return IncomingWebhookProcessingResult.Processed(observed.Id.ToString("N"));
    }
}
