using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripePaymentIncomingWebhookHandler(
    IRegistrationPaymentAttemptRepository paymentAttempts) : IIncomingWebhookHandler
{
    public const string EffectKindValue = "payment-authoritative-reconciliation";

    public string EffectKind => EffectKindValue;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, StripeConnectIncomingWebhookVerifier.ProviderCode, StringComparison.Ordinal) &&
        eventType is global::Stripe.EventTypes.CheckoutSessionCompleted
            or global::Stripe.EventTypes.CheckoutSessionAsyncPaymentSucceeded
            or global::Stripe.EventTypes.CheckoutSessionAsyncPaymentFailed
            or global::Stripe.EventTypes.CheckoutSessionExpired;

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        StripePaymentWebhookEnvelope? envelope;
        try
        {
            envelope = StripePaymentWebhookEnvelope.Deserialize(context.PayloadBytes.Span);
        }
        catch (System.Text.Json.JsonException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_payment_envelope_invalid");
        }

        if (envelope is null ||
            !string.Equals(envelope.EventId, context.ProviderMessageId, StringComparison.Ordinal) ||
            !string.Equals(envelope.EventType, context.EventType, StringComparison.Ordinal) ||
            envelope.CreatedAt.Kind != DateTimeKind.Utc)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_payment_envelope_mismatch");
        }

        Domain.PaymentAttempt? attempt = await paymentAttempts.FindByProviderObjectAsync(
            context.TenantId,
            envelope.AccountId,
            envelope.ObjectId,
            cancellationToken);
        if (attempt is null)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_payment_attempt_orphaned");
        }

        if (!string.Equals(attempt.ProviderApiRevision, envelope.ApiRevision, StringComparison.Ordinal) ||
            !string.Equals(attempt.ProviderCheckoutSessionId, envelope.ObjectId, StringComparison.Ordinal) ||
            !string.Equals(attempt.RecipientSnapshot.ExternalAccountId, envelope.AccountId, StringComparison.Ordinal))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_payment_attempt_context_mismatch");
        }

        await paymentAttempts.EnsureReconciliationDueAsync(
            attempt,
            context.IncomingWebhookMessageId,
            envelope.CreatedAt,
            cancellationToken);
        return IncomingWebhookProcessingResult.Processed(attempt.Id.ToString("N"));
    }
}
