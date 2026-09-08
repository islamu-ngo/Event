using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Contracts.Payments;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripeConnectIncomingWebhookVerifier(
    IOptionsMonitor<WebhookOptions> options,
    IOptions<StripePaymentOptions> stripeOptions,
    ISecretResolver secretResolver,
    IOrganizerPaymentProviderConnectionRepository connectionRepository,
    ILogger<StripeConnectIncomingWebhookVerifier> logger) : IIncomingWebhookVerifier
{
    public const string ProviderCode = "stripe-connect";
    public const string PaymentProviderCode = "stripe";
    private const int HistoricalMatchLimit = 2;

    public string Provider => ProviderCode;

    public async Task<IncomingWebhookVerificationResult> VerifyAsync(
        IncomingWebhookContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Headers.TryGetValue("Stripe-Signature", out string? signature) || string.IsNullOrWhiteSpace(signature))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_signature_missing");
        }

        SecretResolutionResult secret = await secretResolver.ResolveAsync(
            options.CurrentValue.Stripe.ConnectWebhookSecretRef ?? SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
            tenantId: null,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secret?.Value))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_secret_unavailable");
        }

        global::Stripe.Event stripeEvent;
        try
        {
            stripeEvent = global::Stripe.EventUtility.ConstructEvent(
                context.RawPayload,
                signature,
                secret.Value,
                tolerance: 300,
                throwOnApiVersionMismatch: true);
        }
        catch (global::Stripe.StripeException)
        {
            logger.LogWarning("Stripe Connect webhook rejected with safe category {FailureCategory}.", "stripe_connect_signature_invalid");
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_signature_invalid");
        }
        catch (JsonException)
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_payload_invalid");
        }

        if (!IsSupportedEvent(stripeEvent.Type))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_event_unsupported");
        }

        if (!StripeModeEvidence.TryReadEventLivemode(context.RawPayload, out bool eventLivemode))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_event_mode_missing");
        }

        if (!StripeModeEvidence.Matches(stripeOptions.Value, eventLivemode))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_event_mode_mismatch");
        }

        if (!TryGetEventId(stripeEvent, out string? eventId))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_event_id_invalid");
        }

        if (!TryGetAccountId(stripeEvent, out string? accountId))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_account_missing");
        }

        string verifiedAccountId = accountId!;
        if (string.Equals(stripeEvent.Type, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(stripeEvent.Account)
            && !string.Equals(stripeEvent.Account, verifiedAccountId, StringComparison.Ordinal))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_account_mismatch");
        }

        if (!TryGetEventCreatedAt(stripeEvent, out _))
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_event_timestamp_invalid");
        }

        IReadOnlyList<Domain.OrganizerPaymentProviderConnection> matches = await connectionRepository.ListHistoricalByExternalAccountAsync(
            PaymentProviderCode,
            verifiedAccountId,
            HistoricalMatchLimit,
            cancellationToken);
        if (matches.Count != 1)
        {
            return IncomingWebhookVerificationResult.Rejected("stripe_connect_account_not_unique");
        }

        var connection = matches[0];
        if (IsRefundEvent(stripeEvent.Type))
        {
            if (!TryCreateRefundEnvelope(stripeEvent, eventId!, verifiedAccountId, out StripeRefundWebhookEnvelope? envelope))
            {
                return IncomingWebhookVerificationResult.Rejected("stripe_connect_refund_evidence_invalid");
            }

            return IncomingWebhookVerificationResult.VerifiedTenantCredential(
                connection.TenantId,
                eventId!,
                stripeEvent.Type,
                eventId!,
                envelope!.Serialize());
        }
        if (IsDisputeEvent(stripeEvent.Type))
        {
            if (!TryCreateDisputeEnvelope(stripeEvent, eventId!, verifiedAccountId, out StripeDisputeWebhookEnvelope? envelope))
            {
                return IncomingWebhookVerificationResult.Rejected("stripe_connect_dispute_evidence_invalid");
            }

            return IncomingWebhookVerificationResult.VerifiedTenantCredential(
                connection.TenantId,
                eventId!,
                stripeEvent.Type,
                eventId!,
                envelope!.Serialize());
        }
        if (IsPaymentEvent(stripeEvent.Type))
        {
            string? objectId = stripeEvent.Data.Object is global::Stripe.Checkout.Session session
                ? StripeConnectAccountAdapter.BoundedText(session.Id, 200)
                : null;
            if (objectId is null)
            {
                return IncomingWebhookVerificationResult.Rejected("stripe_connect_object_id_invalid");
            }

            var envelope = new StripePaymentWebhookEnvelope(
                eventId!,
                stripeEvent.Type,
                objectId,
                verifiedAccountId,
                eventLivemode,
                stripeEvent.ApiVersion,
                stripeEvent.Created);
            return IncomingWebhookVerificationResult.VerifiedTenantCredential(
                connection.TenantId,
                eventId!,
                stripeEvent.Type,
                $"{stripeEvent.Type}:{objectId}",
                envelope.Serialize());
        }

        return IncomingWebhookVerificationResult.VerifiedTenantCredential(
            connection.TenantId,
            eventId!,
            stripeEvent.Type,
            eventId!);
    }

    private static bool TryGetEventId(global::Stripe.Event stripeEvent, out string? eventId)
    {
        eventId = StripeConnectAccountAdapter.BoundedText(stripeEvent.Id, 120);
        return eventId is not null;
    }

    private static bool TryGetAccountId(global::Stripe.Event stripeEvent, out string? accountId)
    {
        accountId = StripeConnectAccountAdapter.BoundedText(stripeEvent.Account, 200);
        if (!string.IsNullOrWhiteSpace(accountId)
            && !string.Equals(stripeEvent.Type, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal))
        {
            return true;
        }

        if (stripeEvent.Data.Object is global::Stripe.Account account && !string.IsNullOrWhiteSpace(account.Id))
        {
            accountId = account.Id;
            return true;
        }

        return !string.IsNullOrWhiteSpace(accountId);
    }

    private static bool IsSupportedEvent(string? eventType) =>
        string.Equals(eventType, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal)
        || string.Equals(eventType, global::Stripe.EventTypes.AccountApplicationDeauthorized, StringComparison.Ordinal)
        || IsPaymentEvent(eventType)
        || IsRefundEvent(eventType)
        || IsDisputeEvent(eventType);

    internal static bool IsRefundEvent(string? eventType) =>
        eventType is "refund.created" or "refund.updated" or "refund.failed";

    internal static bool IsDisputeEvent(string? eventType) =>
        eventType is "charge.dispute.created" or "charge.dispute.updated" or "charge.dispute.closed";

    private static bool IsPaymentEvent(string? eventType) =>
        string.Equals(eventType, global::Stripe.EventTypes.CheckoutSessionCompleted, StringComparison.Ordinal)
        || string.Equals(eventType, global::Stripe.EventTypes.CheckoutSessionAsyncPaymentSucceeded, StringComparison.Ordinal)
        || string.Equals(eventType, global::Stripe.EventTypes.CheckoutSessionAsyncPaymentFailed, StringComparison.Ordinal)
        || string.Equals(eventType, global::Stripe.EventTypes.CheckoutSessionExpired, StringComparison.Ordinal);

    private static bool TryGetEventCreatedAt(global::Stripe.Event stripeEvent, out DateTime createdAt)
    {
        createdAt = stripeEvent.Created;
        return createdAt != default && createdAt.Kind == DateTimeKind.Utc;
    }

    private static bool TryCreateRefundEnvelope(
        global::Stripe.Event stripeEvent,
        string eventId,
        string accountId,
        out StripeRefundWebhookEnvelope? envelope)
    {
        envelope = null;
        if (stripeEvent.Data.Object is not global::Stripe.Refund refund ||
            refund.Metadata is null ||
            !refund.Metadata.TryGetValue("islamu_refund_attempt_id", out string? attemptValue) ||
            !Guid.TryParse(attemptValue, out Guid attemptId) ||
            StripeConnectAccountAdapter.BoundedText(refund.Id, 200) is not { } refundId ||
            StripeConnectAccountAdapter.BoundedText(refund.PaymentIntentId, 200) is not { } paymentId ||
            NormalizeCurrency(refund.Currency) is not { } currency ||
            MapRefundStatus(refund.Status) is RefundProviderStatus.Unknown ||
            refund.Amount <= 0)
        {
            return false;
        }

        envelope = new(
            eventId,
            stripeEvent.Type,
            attemptId,
            refundId,
            paymentId,
            accountId,
            refund.Amount,
            currency,
            MapRefundStatus(refund.Status),
            stripeEvent.Created);
        return true;
    }

    private static bool TryCreateDisputeEnvelope(
        global::Stripe.Event stripeEvent,
        string eventId,
        string accountId,
        out StripeDisputeWebhookEnvelope? envelope)
    {
        envelope = null;
        if (stripeEvent.Data.Object is not global::Stripe.Dispute dispute ||
            StripeConnectAccountAdapter.BoundedText(dispute.Id, 200) is not { } disputeId ||
            StripeConnectAccountAdapter.BoundedText(dispute.PaymentIntentId, 200) is not { } paymentId ||
            NormalizeCurrency(dispute.Currency) is not { } currency ||
            !TryMapDispute(dispute.Status, out PaymentDisputeStage stage, out PaymentDisputeStatus status) ||
            dispute.Amount <= 0)
        {
            return false;
        }

        envelope = new(
            eventId,
            stripeEvent.Type,
            disputeId,
            paymentId,
            accountId,
            dispute.Amount,
            currency,
            stage,
            status,
            dispute.EvidenceDetails?.DueBy,
            stripeEvent.Created);
        return true;
    }

    private static RefundProviderStatus MapRefundStatus(string? status) => status switch
    {
        "pending" => RefundProviderStatus.Pending,
        "requires_action" => RefundProviderStatus.RequiresAction,
        "succeeded" => RefundProviderStatus.Succeeded,
        "failed" => RefundProviderStatus.Failed,
        "canceled" => RefundProviderStatus.Cancelled,
        _ => RefundProviderStatus.Unknown
    };

    private static bool TryMapDispute(
        string? providerStatus,
        out PaymentDisputeStage stage,
        out PaymentDisputeStatus status)
    {
        stage = providerStatus?.StartsWith("warning_", StringComparison.Ordinal) == true
            ? PaymentDisputeStage.Inquiry
            : PaymentDisputeStage.Formal;
        status = providerStatus switch
        {
            "warning_needs_response" or "warning_under_review" or "needs_response" or "under_review" => PaymentDisputeStatus.Open,
            "won" => PaymentDisputeStatus.Won,
            "lost" => PaymentDisputeStatus.Lost,
            "warning_closed" => PaymentDisputeStatus.Withdrawn,
            "prevented" => PaymentDisputeStatus.Prevented,
            _ => default
        };
        return status != default;
    }

    private static string? NormalizeCurrency(string? value)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : null;
    }
}
