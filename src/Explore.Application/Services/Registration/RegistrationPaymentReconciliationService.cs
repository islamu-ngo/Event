using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed record RegistrationPaymentReconciliationRequest(
    string LeaseOwner,
    int BatchSize = 50,
    TimeSpan? LeaseDuration = null);

public sealed record RegistrationPaymentReconciliationResult(
    int Claimed,
    int Succeeded,
    int NonTerminal,
    int Unknown,
    int Parked,
    int Stale,
    int RequeuedDispatches = 0);

public sealed class RegistrationPaymentReconciliationService(
    IRegistrationPaymentAttemptRepository repository,
    IHostedCheckoutSessionRetriever checkoutRetriever,
    IPaymentIntentRetriever paymentIntentRetriever,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NonTerminalRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnknownRetryDelay = TimeSpan.FromMinutes(2);

    public async Task<RegistrationPaymentReconciliationResult> ReconcileDueAsync(
        RegistrationPaymentReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        int claimed = 0;
        int succeeded = 0;
        int nonTerminal = 0;
        int unknown = 0;
        int parked = 0;
        int stale = 0;
        int requeuedDispatches = 0;
        if (string.IsNullOrWhiteSpace(request.LeaseOwner) || request.BatchSize is < 1 or > 50 ||
            request.LeaseDuration is { } requestedLease && requestedLease <= TimeSpan.Zero)
        {
            throw new ArgumentException("Payment reconciliation requests require a lease owner and batch size from 1 through 50.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTime claimTime = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<PaymentReconciliationClaim> claims = await repository.ClaimDueReconciliationsAsync(
            request.LeaseOwner,
            request.BatchSize,
            claimTime,
            request.LeaseDuration ?? DefaultLeaseDuration,
            cancellationToken);
        foreach (PaymentReconciliationClaim claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            claimed++;
            PaymentAttempt? attempt = await repository.GetReconciliationAttemptAsync(claim, now, cancellationToken);
            if (string.IsNullOrWhiteSpace(attempt?.ProviderCheckoutSessionId))
            {
                DateTime recoveryObservedAt = attempt is null ? now : NextObservation(attempt, now);
                bool requeued = await repository.RequeueLatestUnknownDispatchAsync(
                    claim.TenantId,
                    claim.PaymentAttemptId,
                    recoveryObservedAt,
                    recoveryObservedAt,
                    cancellationToken);
                if (requeued)
                {
                    requeuedDispatches++;
                }
                stale += await Settle(
                    claim,
                    Retry(
                        PaymentAttemptStatusEnum.Unknown,
                        "payment_reconciliation_awaiting_idempotent_dispatch",
                        recoveryObservedAt,
                        UnknownRetryDelay,
                        attempt?.LastProviderRequestId),
                    cancellationToken) ? 0 : 1;
                unknown++;
                continue;
            }

            string checkoutSessionId = attempt.ProviderCheckoutSessionId;

            HostedCheckoutRetrieveResult checkout = await checkoutRetriever.RetrieveAsync(
                HostedCheckoutRetrieveRequest.Create(attempt.ProviderCode, attempt.RecipientSnapshot.ExternalAccountId, checkoutSessionId),
                cancellationToken);
            DateTime observedAt = NextObservation(attempt, timeProvider.GetUtcNow().UtcDateTime);
            if (checkout.Outcome != HostedCheckoutOperationOutcome.Succeeded || checkout.Session is not { } session)
            {
                bool isConfiguration = checkout.Failure?.Kind == HostedCheckoutFailureKind.Configuration;
                bool isPermanent = checkout.Failure?.Kind == HostedCheckoutFailureKind.ProviderRejected;
                PaymentReconciliationDecision decision = isPermanent
                    ? Park(PaymentAttemptStatusEnum.Unknown, Code(checkout.Failure?.Code), observedAt, checkout.ProviderRequestId)
                    : Retry(PaymentAttemptStatusEnum.Unknown, Code(checkout.Failure?.Code), observedAt, isConfiguration ? TimeSpan.FromMinutes(15) : UnknownRetryDelay, checkout.ProviderRequestId);
                stale += await Settle(claim, decision, cancellationToken) ? 0 : 1;
                if (isPermanent) parked++; else unknown++;
                continue;
            }

            if (!SessionMatches(attempt, session))
            {
                stale += await Settle(claim, Park(PaymentAttemptStatusEnum.Unknown, "payment_reconciliation_checkout_mismatch", observedAt, checkout.ProviderRequestId), cancellationToken) ? 0 : 1;
                parked++;
                continue;
            }

            if (session.Status == HostedCheckoutSessionStatus.Expired &&
                session.PaymentStatus == HostedCheckoutPaymentStatus.Unpaid &&
                session.PaymentId is null)
            {
                stale += await Settle(
                    claim,
                    Complete(PaymentAttemptStatusEnum.Cancelled, session, null, observedAt, checkout.ProviderRequestId),
                    cancellationToken) ? 0 : 1;
                nonTerminal++;
                continue;
            }

            if (session.PaymentId is null)
            {
                bool ambiguousCompletion = session.Status == HostedCheckoutSessionStatus.Complete ||
                                           session.PaymentStatus == HostedCheckoutPaymentStatus.Paid;
                PaymentReconciliationDecision decision = ambiguousCompletion
                    ? Retry(PaymentAttemptStatusEnum.Unknown, "payment_reconciliation_payment_id_missing", observedAt, UnknownRetryDelay, checkout.ProviderRequestId, session.SessionId)
                    : Retry(PaymentAttemptStatusEnum.RequiresAction, "payment_reconciliation_buyer_action", observedAt, NonTerminalRetryDelay, checkout.ProviderRequestId, session.SessionId);
                stale += await Settle(claim, decision, cancellationToken) ? 0 : 1;
                if (ambiguousCompletion) unknown++; else nonTerminal++;
                continue;
            }

            PaymentIntentRetrieveResult payment = await paymentIntentRetriever.RetrievePaymentIntentAsync(
                PaymentIntentRetrieveRequest.Create(attempt.ProviderCode, attempt.RecipientSnapshot.ExternalAccountId, session.PaymentId),
                cancellationToken);
            observedAt = NextObservation(attempt, timeProvider.GetUtcNow().UtcDateTime);
            if (payment.Outcome != HostedCheckoutOperationOutcome.Succeeded || payment.PaymentIntent is not { } intent)
            {
                PaymentReconciliationDecision decision = Retry(
                    PaymentAttemptStatusEnum.Unknown,
                    Code(payment.Failure?.Code),
                    observedAt,
                    UnknownRetryDelay,
                    payment.ProviderRequestId ?? checkout.ProviderRequestId,
                    session.SessionId,
                    session.PaymentId);
                stale += await Settle(claim, decision, cancellationToken) ? 0 : 1;
                unknown++;
                continue;
            }

            if (!PaymentMatches(attempt, session, intent))
            {
                stale += await Settle(claim, Park(PaymentAttemptStatusEnum.Unknown, "payment_reconciliation_money_mismatch", observedAt, payment.ProviderRequestId), cancellationToken) ? 0 : 1;
                parked++;
                continue;
            }

            PaymentReconciliationDecision authoritative = Map(session, intent, observedAt, payment.ProviderRequestId);
            bool settled = await Settle(claim, authoritative, cancellationToken);
            stale += settled ? 0 : 1;
            if (authoritative.Status == PaymentAttemptStatusEnum.Succeeded) succeeded++;
            else if (authoritative.Status == PaymentAttemptStatusEnum.Unknown) unknown++;
            else nonTerminal++;
        }

        return new(claimed, succeeded, nonTerminal, unknown, parked, stale, requeuedDispatches);
    }

    private static PaymentReconciliationDecision Map(
        HostedCheckoutSession session,
        PaymentIntentObservation intent,
        DateTime observedAt,
        string? requestId) => intent.Status switch
        {
            PaymentIntentStatus.Succeeded when session.PaymentStatus == HostedCheckoutPaymentStatus.Paid =>
                Complete(PaymentAttemptStatusEnum.Succeeded, session, intent.PaymentIntentId, observedAt, requestId),
            PaymentIntentStatus.Canceled =>
                Complete(PaymentAttemptStatusEnum.Failed, session, intent.PaymentIntentId, observedAt, requestId),
            PaymentIntentStatus.RequiresPaymentMethod when
                session.Status == HostedCheckoutSessionStatus.Complete &&
                session.PaymentStatus == HostedCheckoutPaymentStatus.Unpaid =>
                Complete(PaymentAttemptStatusEnum.Failed, session, intent.PaymentIntentId, observedAt, requestId),
            PaymentIntentStatus.RequiresPaymentMethod or PaymentIntentStatus.RequiresConfirmation or PaymentIntentStatus.RequiresAction =>
                Retry(PaymentAttemptStatusEnum.RequiresAction, "payment_reconciliation_buyer_action", observedAt, NonTerminalRetryDelay, requestId, session.SessionId, intent.PaymentIntentId),
            PaymentIntentStatus.Processing or PaymentIntentStatus.RequiresCapture =>
                Retry(PaymentAttemptStatusEnum.Processing, "payment_reconciliation_processing", observedAt, NonTerminalRetryDelay, requestId, session.SessionId, intent.PaymentIntentId),
            _ => Retry(PaymentAttemptStatusEnum.Unknown, "payment_reconciliation_provider_ambiguous", observedAt, UnknownRetryDelay, requestId, session.SessionId, intent.PaymentIntentId)
        };

    private static bool SessionMatches(PaymentAttempt attempt, HostedCheckoutSession session) =>
        string.Equals(session.SessionId, attempt.ProviderCheckoutSessionId, StringComparison.Ordinal) &&
        session.AmountTotalMinor == attempt.TotalMinor &&
        string.Equals(session.CurrencyCode, attempt.CurrencyCode, StringComparison.Ordinal);

    private static bool PaymentMatches(PaymentAttempt attempt, HostedCheckoutSession session, PaymentIntentObservation intent) =>
        string.Equals(session.PaymentId, intent.PaymentIntentId, StringComparison.Ordinal) &&
        intent.AmountMinor == attempt.TotalMinor &&
        string.Equals(intent.CurrencyCode, attempt.CurrencyCode, StringComparison.Ordinal) &&
        intent.ApplicationFeeMinor == checked(attempt.PlatformFeeMinor + attempt.PlatformContributionMinor);

    private static PaymentReconciliationDecision Complete(PaymentAttemptStatusEnum status, HostedCheckoutSession session, string? paymentId, DateTime at, string? requestId) =>
        new(PaymentReconciliationDisposition.Complete, status, session.SessionId, paymentId, requestId, string.Empty, at);

    private static PaymentReconciliationDecision Retry(PaymentAttemptStatusEnum status, string code, DateTime at, TimeSpan delay, string? requestId, string? sessionId = null, string? paymentId = null) =>
        new(PaymentReconciliationDisposition.Retry, status, sessionId, paymentId, requestId, code, at, at.Add(delay));

    private static PaymentReconciliationDecision Park(PaymentAttemptStatusEnum status, string code, DateTime at, string? requestId = null) =>
        new(PaymentReconciliationDisposition.Park, status, null, null, requestId, code, at);

    private Task<bool> Settle(PaymentReconciliationClaim claim, PaymentReconciliationDecision decision, CancellationToken cancellationToken) =>
        repository.SettleReconciliationAsync(claim, decision, cancellationToken);

    private static DateTime NextObservation(PaymentAttempt attempt, DateTime now) => now > attempt.LastStatusObservedAt ? now : attempt.LastStatusObservedAt.AddTicks(1);

    private static string Code(string? value) => string.IsNullOrWhiteSpace(value) ? "payment_reconciliation_provider_unknown" : value;
}
