using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using RegistrationPaymentCheckoutDispatchRequest = Explore.Application.Contracts.Payments.RegistrationPaymentCheckoutDispatchRequest;
using RegistrationPaymentCheckoutDispatchResult = Explore.Application.Contracts.Payments.RegistrationPaymentCheckoutDispatchResult;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public sealed class RegistrationPaymentCheckoutDispatchService(
    ICheckoutDispatchAdmission admission,
    ICheckoutDispatchExecutionScopeFactory executions,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ProviderCutoffMinimum = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProviderCutoffTransportMargin = TimeSpan.FromMinutes(1);
    public const int MaxPreHandoffAttempts = 3;
    private const int InitialPreHandoffRetrySeconds = 5;
    private const int MaxPreHandoffRetrySeconds = 60;

    public async Task<RegistrationPaymentCheckoutDispatchResult> DeferDueForConfigurationAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        string failureCode,
        CancellationToken cancellationToken)
    {
        int claimed = 0;
        int retried = 0;
        int stale = 0;
        for (int index = 0; index < batchSize; index++)
        {
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            IReadOnlyList<CheckoutDispatchClaim> claims = await admission.ClaimDueDispatchEffectsAsync(
                leaseOwner, 1, now, leaseDuration, cancellationToken);
            if (claims.Count == 0)
            {
                break;
            }

            claimed++;
            await using var execution = await executions.CreateAsync(claims[0], now, cancellationToken);
            if (execution is null)
            {
                stale++;
                continue;
            }
            var repository = execution.Repository;
            CheckoutDispatchConfigurationDisposition disposition = await repository.DeferCheckoutDispatchForConfigurationAsync(
                claims[0],
                failureCode,
                now.AddMinutes(5),
                now,
                cancellationToken);
            disposition = await ResolveConfigurationDispositionAsync(execution.OrderLifecycle, claims[0], disposition, now, cancellationToken);
            bool deferred = disposition is CheckoutDispatchConfigurationDisposition.Deferred or CheckoutDispatchConfigurationDisposition.CancelledExpired;
            Increment(deferred, ref retried, ref stale);
        }

        return new(claimed, 0, 0, 0, retried, stale);
    }

    public async Task<RegistrationPaymentCheckoutDispatchResult> DispatchDueAsync(
        RegistrationPaymentCheckoutDispatchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        HostedCheckoutReturnUrls returnUrls = HostedCheckoutReturnUrls.Create(
            request.AllowedReturnOrigin,
            request.SuccessUrl,
            request.CancelUrl);

        int claimed = 0;
        int completed = 0;
        int unknown = 0;
        int parked = 0;
        int retried = 0;
        int stale = 0;
        for (int index = 0; index < request.BatchSize; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (claimed > 0)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            IReadOnlyList<CheckoutDispatchClaim> claims = await admission.ClaimDueDispatchEffectsAsync(
                request.LeaseOwner, 1, now, request.LeaseDuration, cancellationToken);
            if (claims.Count == 0)
            {
                break;
            }

            claimed++;
            CheckoutDispatchClaim claim = claims[0];
            await using var execution = await executions.CreateAsync(claim, now, cancellationToken);
            if (execution is null)
            {
                stale++;
                continue;
            }
            var repository = execution.Repository;
            var checkoutCreator = execution.CheckoutCreator;
            var checkoutRetriever = execution.CheckoutRetriever;
            var paymentIntentRetriever = execution.PaymentIntentRetriever;
            var checkoutActivation = execution.CheckoutActivation;
            var acceptanceFreshness = execution.AcceptanceFreshness;
            PaymentAttempt? attempt = await repository.GetClaimedAttemptAsync(claim, now, cancellationToken);
            if (attempt is null)
            {
                stale++;
                continue;
            }

            PaymentAttemptStatusEnum status = (PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId;
            if (status == PaymentAttemptStatusEnum.Unknown && attempt.ProviderCheckoutSessionId is { } redriveSessionId)
            {
                HostedCheckoutRetrieveResult retrieved = await checkoutRetriever.RetrieveAsync(
                    HostedCheckoutRetrieveRequest.Create(attempt.ProviderCode, attempt.RecipientSnapshot.ExternalAccountId, redriveSessionId),
                    cancellationToken);
                PaymentIntentRetrieveResult? paymentIntent = null;
                if (retrieved.Outcome == HostedCheckoutOperationOutcome.Succeeded &&
                    retrieved.Session is { PaymentId: not null } retrievedSession &&
                    MoneyMatches(attempt, retrievedSession))
                {
                    paymentIntent = await paymentIntentRetriever.RetrievePaymentIntentAsync(
                        PaymentIntentRetrieveRequest.Create(
                            attempt.ProviderCode,
                            attempt.RecipientSnapshot.ExternalAccountId,
                            retrievedSession.PaymentId),
                        cancellationToken);
                }

                DateTime retrievedAt = NextObservation(now);
                if (retrieved.Session is { } session &&
                    paymentIntent is { Outcome: HostedCheckoutOperationOutcome.Succeeded, PaymentIntent: { } intent } &&
                    PaymentIntentMoneyMatches(attempt, intent))
                {
                    Increment(
                        await repository.CompleteCheckoutDispatchAsync(
                            claim, session.SessionId, paymentIntent.ProviderRequestId, retrievedAt, CancellationToken.None),
                        ref completed,
                        ref stale);
                }
                else
                {
                    Increment(
                        await repository.MarkCheckoutDispatchUnknownAsync(
                            claim, retrieved.ProviderRequestId, retrievedAt, CancellationToken.None),
                        ref unknown,
                        ref stale);
                }

                continue;
            }

            if (attempt.ProviderCheckoutSessionId is { } existingSessionId)
            {
                bool settled = await repository.CompleteCheckoutDispatchAsync(
                    claim,
                    existingSessionId,
                    attempt.LastProviderRequestId,
                    now,
                    cancellationToken);
                Increment(settled, ref completed, ref stale);
                continue;
            }

            if (status is PaymentAttemptStatusEnum.Cancelled or PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Succeeded)
            {
                bool settled = await repository.FailCheckoutDispatchAsync(
                    claim,
                    status == PaymentAttemptStatusEnum.Cancelled ? "checkout_attempt_cancelled" : "checkout_attempt_terminal",
                    attempt.LastProviderRequestId,
                    now,
                    cancellationToken);
                Increment(settled, ref parked, ref stale);
                continue;
            }

            bool unknownReplay = status == PaymentAttemptStatusEnum.Unknown ||
                                 claim.ReplayKind == CheckoutDispatchReplayKind.UnknownRedrive;
            if (unknownReplay && !AllowsUnknownReplay(execution.PaymentProviderDescriptor, attempt, now))
            {
                bool settled = await repository.MarkCheckoutDispatchUnknownAsync(
                    claim, attempt.LastProviderRequestId, now, CancellationToken.None);
                Increment(settled, ref unknown, ref stale);
                continue;
            }

            bool safeReplay = unknownReplay || claim.ReplayKind == CheckoutDispatchReplayKind.PreHandoffRetry;
            if (status is PaymentAttemptStatusEnum.DispatchPending or PaymentAttemptStatusEnum.Unknown && !safeReplay)
            {
                bool settled = await repository.MarkCheckoutDispatchUnknownAsync(
                    claim,
                    attempt.LastProviderRequestId,
                    now,
                    CancellationToken.None);
                Increment(settled, ref unknown, ref stale);
                continue;
            }

            if (attempt.AcceptanceSnapshot is not { } acceptance)
            {
                CheckoutDispatchConfigurationDisposition disposition = await DeferActivationAsync(
                    execution, claim, "payment_acceptance_missing", now, cancellationToken);
                CountDisposition(disposition, ref retried, ref parked, ref stale);
                continue;
            }

            PaidCheckoutActivationResult activation = await checkoutActivation.EvaluateAsync(
                new(attempt.TenantId, acceptance.EventId, attempt.CurrencyCode, attempt.TotalMinor, now, attempt.Id), cancellationToken);
            if (!activation.IsActive)
            {
                CheckoutDispatchConfigurationDisposition disposition = await DeferActivationAsync(
                    execution, claim, activation.FailureCode ?? "payment_activation_unavailable", now, cancellationToken);
                CountDisposition(disposition, ref retried, ref parked, ref stale);
                continue;
            }

            if (!await acceptanceFreshness.IsCurrentAsync(attempt, cancellationToken))
            {
                CheckoutDispatchConfigurationDisposition disposition = await DeferActivationAsync(
                    execution, claim, "payment_acceptance_stale", now, cancellationToken);
                CountDisposition(disposition, ref retried, ref parked, ref stale);
                continue;
            }

            DateTime preparedAt = timeProvider.GetUtcNow().UtcDateTime;
            DateTime minimumCutoff = preparedAt.Add(ProviderCutoffMinimum).Add(ProviderCutoffTransportMargin);
            attempt = await repository.PrepareCheckoutDispatchAsync(claim, preparedAt, minimumCutoff, cancellationToken);
            if (attempt is null)
            {
                stale++;
                continue;
            }

            DateTime handoffAt = timeProvider.GetUtcNow().UtcDateTime;
            if (unknownReplay && !AllowsUnknownReplay(execution.PaymentProviderDescriptor, attempt, handoffAt))
            {
                bool settled = await repository.MarkCheckoutDispatchUnknownAsync(
                    claim, attempt.LastProviderRequestId, handoffAt, CancellationToken.None);
                Increment(settled, ref unknown, ref stale);
                continue;
            }

            long applicationFeeMinor = checked(attempt.PlatformFeeMinor + attempt.PlatformContributionMinor);
            HostedCheckoutCreateRequest providerRequest = HostedCheckoutCreateRequest.Create(
                attempt.Id,
                attempt.RegistrationOrderId,
                attempt.ProviderCode,
                attempt.RecipientSnapshot.ExternalAccountId,
                attempt.ProviderIdempotencyKey,
                attempt.CurrencyCode,
                attempt.TotalMinor,
                applicationFeeMinor,
                attempt.ExpiresAt ?? throw new InvalidOperationException("Payment attempt cutoff is required before provider handoff."),
                request.AllowedReturnOrigin,
                returnUrls.SuccessUrl,
                returnUrls.CancelUrl);
            HostedCheckoutCreateResult providerResult = await checkoutCreator.CreateAsync(providerRequest, cancellationToken);
            CancellationToken settlementToken = CancellationToken.None;
            DateTime providerObservedAt = timeProvider.GetUtcNow().UtcDateTime;
            DateTime settledAt = providerObservedAt > preparedAt ? providerObservedAt : preparedAt.AddTicks(1);
            switch (providerResult.Outcome)
            {
                case HostedCheckoutOperationOutcome.Succeeded when providerResult.Session is not null:
                    Increment(
                        await repository.CompleteCheckoutDispatchAsync(
                            claim,
                            providerResult.Session.SessionId,
                            providerResult.ProviderRequestId,
                            settledAt,
                            settlementToken),
                        ref completed,
                        ref stale);
                    break;
                case HostedCheckoutOperationOutcome.Unknown:
                    Increment(
                        await repository.MarkCheckoutDispatchUnknownAsync(
                            claim,
                            providerResult.ProviderRequestId,
                            settledAt,
                            settlementToken),
                        ref unknown,
                        ref stale);
                    break;
                case HostedCheckoutOperationOutcome.Failed when providerResult.Failure is { ProviderHandoffStarted: false } preHandoff:
                    string preHandoffCode = BoundedFailureCode(preHandoff.Code);
                    CheckoutDispatchConfigurationDisposition disposition = await repository.DeferCheckoutDispatchForConfigurationAsync(
                        claim,
                        preHandoffCode,
                        settledAt.Add(preHandoff.PreHandoffDisposition == HostedCheckoutPreHandoffDisposition.Transient && claim.AttemptCount < MaxPreHandoffAttempts
                            ? PreHandoffRetryDelay(claim.AttemptCount)
                            : TimeSpan.FromMinutes(15)),
                        settledAt,
                        settlementToken);
                    disposition = await ResolveConfigurationDispositionAsync(execution.OrderLifecycle, claim, disposition, settledAt, settlementToken);
                    if (disposition == CheckoutDispatchConfigurationDisposition.Deferred) retried++;
                    else if (disposition == CheckoutDispatchConfigurationDisposition.CancelledExpired) parked++;
                    else stale++;
                    break;
                case HostedCheckoutOperationOutcome.Failed when providerResult.Failure is { Kind: HostedCheckoutFailureKind.Configuration } configuration:
                    CheckoutDispatchConfigurationDisposition configurationDisposition = await repository.DeferCheckoutDispatchForConfigurationAsync(
                        claim,
                        BoundedFailureCode(configuration.Code),
                        settledAt.AddMinutes(15),
                        settledAt,
                        settlementToken);
                    configurationDisposition = await ResolveConfigurationDispositionAsync(
                        execution.OrderLifecycle,
                        claim,
                        configurationDisposition,
                        settledAt,
                        settlementToken);
                    if (configurationDisposition == CheckoutDispatchConfigurationDisposition.Deferred) retried++;
                    else if (configurationDisposition == CheckoutDispatchConfigurationDisposition.CancelledExpired) parked++;
                    else stale++;
                    break;
                default:
                    Increment(
                        await repository.FailCheckoutDispatchAsync(
                            claim,
                            BoundedFailureCode(providerResult.Failure?.Code),
                            providerResult.ProviderRequestId,
                            settledAt,
                            settlementToken),
                        ref parked,
                        ref stale);
                    break;
            }
        }

        return new(claimed, completed, unknown, parked, retried, stale);
    }

    private static bool AllowsUnknownReplay(IPaymentProviderDescriptor paymentProviderDescriptor, PaymentAttempt attempt, DateTime observedAt)
    {
        PaymentProviderDescriptor descriptor = paymentProviderDescriptor.Describe();
        return descriptor.ProviderCode == attempt.ProviderCode && descriptor.ProfileCode == attempt.ProfileCode &&
               descriptor.ApiRevision == attempt.ProviderApiRevision &&
               descriptor.CheckoutMutationIdempotencyRetentionFloor is { } retention &&
               retention > ProviderCutoffTransportMargin && observedAt >= attempt.CreatedAt &&
               observedAt - attempt.CreatedAt < retention - ProviderCutoffTransportMargin;
    }

    private static void ValidateRequest(RegistrationPaymentCheckoutDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LeaseOwner) || request.LeaseOwner.Trim().Length > CheckoutDispatchEffect.MaxLeaseOwnerLength ||
            request.BatchSize is < 1 or > 1000 || request.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Checkout dispatch request is invalid.", nameof(request));
        }
    }

    private static string BoundedFailureCode(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= CheckoutDispatchEffect.MaxFailureCodeLength && !normalized.Any(char.IsControl)
            ? normalized
            : "checkout_provider_rejected";
    }

    private static void Increment(bool settled, ref int outcome, ref int stale)
    {
        if (settled)
        {
            outcome++;
        }
        else
        {
            stale++;
        }
    }

    private DateTime NextObservation(DateTime previous)
    {
        DateTime observed = timeProvider.GetUtcNow().UtcDateTime;
        return observed > previous ? observed : previous.AddTicks(1);
    }

    private static bool MoneyMatches(PaymentAttempt attempt, HostedCheckoutSession session) =>
        session.AmountTotalMinor == attempt.TotalMinor &&
        string.Equals(session.CurrencyCode, attempt.CurrencyCode, StringComparison.Ordinal);

    private static bool PaymentIntentMoneyMatches(PaymentAttempt attempt, PaymentIntentObservation paymentIntent) =>
        paymentIntent.AmountMinor == attempt.TotalMinor &&
        paymentIntent.ApplicationFeeMinor == checked(attempt.PlatformFeeMinor + attempt.PlatformContributionMinor) &&
        string.Equals(paymentIntent.CurrencyCode, attempt.CurrencyCode, StringComparison.Ordinal);

    private static TimeSpan PreHandoffRetryDelay(int attemptCount)
    {
        int exponent = Math.Clamp(attemptCount - 1, 0, 4);
        int seconds = Math.Min(MaxPreHandoffRetrySeconds, InitialPreHandoffRetrySeconds * (1 << exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<CheckoutDispatchConfigurationDisposition> DeferActivationAsync(
        ICheckoutDispatchExecutionScope execution,
        CheckoutDispatchClaim claim,
        string failureCode,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        CheckoutDispatchConfigurationDisposition disposition = await execution.Repository.DeferCheckoutDispatchForConfigurationAsync(
            claim, failureCode, observedAt.AddMinutes(5), observedAt, cancellationToken);
        return await ResolveConfigurationDispositionAsync(execution.OrderLifecycle, claim, disposition, observedAt, cancellationToken);
    }

    private static void CountDisposition(
        CheckoutDispatchConfigurationDisposition disposition,
        ref int retried,
        ref int parked,
        ref int stale)
    {
        if (disposition == CheckoutDispatchConfigurationDisposition.Deferred) retried++;
        else if (disposition == CheckoutDispatchConfigurationDisposition.CancelledExpired) parked++;
        else stale++;
    }

    private static Task<CheckoutDispatchConfigurationDisposition> ResolveConfigurationDispositionAsync(
        IRegistrationOrderLifecycleService orderLifecycle,
        CheckoutDispatchClaim claim,
        CheckoutDispatchConfigurationDisposition disposition,
        DateTime observedAt,
        CancellationToken cancellationToken) =>
        disposition == CheckoutDispatchConfigurationDisposition.RequiresLifecycleCancellation
            ? orderLifecycle.CancelExpiredConfigurationBlockedPaymentAsync(claim, observedAt, cancellationToken)
            : Task.FromResult(disposition);
}
