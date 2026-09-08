using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripeConnectIncomingWebhookHandler(
    IOrganizerPaymentProviderConnectionRepository connectionRepository,
    IOptions<StripePaymentOptions> options) : IIncomingWebhookHandler
{
    public const string EffectKindValue = "stripe-connect-account-readiness";
    private const string OrganizerPaymentProviderCode = "stripe";

    public string EffectKind => EffectKindValue;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, StripeConnectIncomingWebhookVerifier.ProviderCode, StringComparison.Ordinal)
        && (string.Equals(eventType, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal)
            || string.Equals(eventType, global::Stripe.EventTypes.AccountApplicationDeauthorized, StringComparison.Ordinal));

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        string payload = Encoding.UTF8.GetString(context.PayloadBytes.Span);
        global::Stripe.Event stripeEvent;
        try
        {
            stripeEvent = global::Stripe.EventUtility.ConstructEventWithoutVerification(payload, throwOnApiVersionMismatch: true);
        }
        catch (global::Stripe.StripeException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_payload_invalid");
        }
        catch (System.Text.Json.JsonException)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_payload_invalid");
        }
        if (!string.Equals(context.ProviderMessageId, stripeEvent.Id, StringComparison.Ordinal)
            || !string.Equals(context.EventType, stripeEvent.Type, StringComparison.Ordinal))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_context_mismatch");
        }

        if (!StripeModeEvidence.TryReadEventLivemode(payload, out bool eventLivemode))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_event_mode_missing");
        }

        if (!StripeModeEvidence.Matches(options.Value, eventLivemode))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_event_mode_mismatch");
        }

        if (!TryGetEventCreatedAt(stripeEvent, out DateTime observedAt))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_event_timestamp_invalid");
        }

        if (!TryGetAccountId(stripeEvent, out string? accountId))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_account_missing");
        }

        string verifiedAccountId = accountId!;
        global::Stripe.Account? account = stripeEvent.Data.Object as global::Stripe.Account;
        if (account is not null
            && !string.IsNullOrWhiteSpace(stripeEvent.Account)
            && !string.Equals(stripeEvent.Account, account.Id, StringComparison.Ordinal))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_account_mismatch");
        }

        if (string.Equals(stripeEvent.Type, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal) && account is null)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_account_missing");
        }

        if (string.Equals(stripeEvent.Type, global::Stripe.EventTypes.AccountUpdated, StringComparison.Ordinal)
            && !TryValidateAccountMode(payload, out string modeFailureCode))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent(modeFailureCode);
        }

        OrganizerPaymentProviderConnection? connection = await connectionRepository.GetByTenantProviderAndExternalAccountForUpdateAsync(
            context.TenantId,
            OrganizerPaymentProviderCode,
            verifiedAccountId,
            cancellationToken);
        if (connection is null)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent("stripe_connect_connection_missing");
        }

        if (connection.StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced)
        {
            return IncomingWebhookProcessingResult.Ignored("stripe_connect_connection_terminal");
        }

        OrganizerPaymentProviderReadiness readiness = string.Equals(stripeEvent.Type, global::Stripe.EventTypes.AccountApplicationDeauthorized, StringComparison.Ordinal)
            ? StripeConnectReadinessMapper.MapDeauthorized(verifiedAccountId, stripeEvent.Id, observedAt)
            : StripeConnectReadinessMapper.MapAccountUpdated(account!, stripeEvent.Id, observedAt);

        if (connection.LastReadinessObservedAt is { } existingObservedAt && readiness.ObservedAt <= existingObservedAt)
        {
            return IncomingWebhookProcessingResult.Ignored("stripe_connect_readiness_stale");
        }

        connection.ApplyReadiness(OrganizerPaymentReadinessMapper.ToObservation(readiness));
        await connectionRepository.SaveChangesAsync(cancellationToken);
        return IncomingWebhookProcessingResult.Processed(connection.Id.ToString("N"));
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

    private static bool TryGetEventCreatedAt(global::Stripe.Event stripeEvent, out DateTime createdAt)
    {
        createdAt = stripeEvent.Created;
        return createdAt != default && createdAt.Kind == DateTimeKind.Utc;
    }

    private bool TryValidateAccountMode(string payload, out string failureCode)
    {
        if (!StripeModeEvidence.TryReadAccountObjectLivemode(payload, out bool accountLivemode))
        {
            failureCode = "stripe_connect_account_mode_missing";
            return false;
        }

        if (!StripeModeEvidence.Matches(options.Value, accountLivemode))
        {
            failureCode = "stripe_connect_account_mode_mismatch";
            return false;
        }

        failureCode = string.Empty;
        return true;
    }
}
