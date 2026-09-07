using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Payments;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationPaymentContractService(
    IRegistrationPaymentAttemptRepository attempts,
    IRegistrationFinalizationRepository finalization,
    RegistrationPaymentAttemptClaimService claims,
    IPaidOrderAcceptanceService acceptances,
    IPaidOrderAcceptanceFreshnessService acceptanceFreshness,
    IRefundAttemptRepository refunds,
    IRegistrationMaterialChangeChoiceRepository materialChangeChoices,
    IHostedCheckoutSessionRetriever checkoutRetriever,
    TimeProvider timeProvider)
{
    public Task<PaidOrderAcceptanceResult> GetAcceptanceDisclosureAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken) => acceptances.DescribeAsync(order, cancellationToken);

    public async Task<RegistrationPaymentCommandResultDto> StartAsync(
        RegistrationOrder order,
        PaidOrderAcceptanceAcknowledgementDto? acknowledgement,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (!IsCurrentlyPayable(order, now))
        {
            return Failure("not_payable", "Registration order is not payable.");
        }

        PaidOrderAcceptanceResult accepted = await acceptances.AcceptAsync(order, acknowledgement, now, cancellationToken);
        if (accepted.Snapshot is not { } acceptance)
        {
            return Failure(accepted.FailureCode ?? "payment_acceptance_required", accepted.Message);
        }

        RegistrationPaymentAttemptClaimResult result = await claims.ClaimAsync(
            new(order.TenantId, order.Id, now, AcceptanceSnapshot: acceptance), cancellationToken);
        if (!result.Success || result.Attempt is null || result.DispatchEffect is null)
        {
            return Failure(result.FailureCode ?? "payment_start_failed", result.Message);
        }

        return Success(Map(order, result.Attempt, result.DispatchEffect));
    }

    public async Task<RegistrationPaymentDto?> GetAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken,
        bool buyerRefundAllowed = false,
        bool organizerRefundAllowed = false)
    {
        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? row = await attempts.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        SucceededPaymentLookupResult succeeded = await finalization.GetSucceededPaymentAsync(
            order.TenantId, order.Id, cancellationToken);
        bool conflict = succeeded.Status == SucceededPaymentLookupStatus.Conflict;
        bool retryAvailable = !conflict &&
            CanRetry(order, row.Value.Attempt, row.Value.DispatchEffect, timeProvider.GetUtcNow().UtcDateTime) &&
            await acceptanceFreshness.IsCurrentAsync(row.Value.Attempt, cancellationToken);
        RegistrationPaymentDto result = Map(
            order,
            row.Value.Attempt,
            row.Value.DispatchEffect,
            retryAvailable,
            failureCode: conflict ? SucceededPaymentLookupResult.DuplicateCode : null,
            needsReconciliation: conflict);
        IReadOnlyList<RefundAttempt> refundAttempts = await refunds.GetByPaymentAsync(
            order.TenantId, row.Value.Attempt.Id, cancellationToken);
        IReadOnlyList<PaymentDispute> disputes = await refunds.GetDisputesAsync(
            order.TenantId, row.Value.Attempt.Id, cancellationToken);
        IReadOnlyList<RegistrationRefundDto> refundDtos = refundAttempts.Select(MapRefund).ToArray();
        IReadOnlyList<RegistrationPaymentDisputeDto> disputeDtos = disputes.Select(dispute => new RegistrationPaymentDisputeDto
        {
            Id = dispute.Id,
            StageCode = dispute.Stage.ToString(),
            StatusCode = dispute.Status.ToString(),
            AmountMinor = dispute.AmountMinor,
            CurrencyCode = dispute.CurrencyCode,
            LastObservedAt = dispute.LastObservedAt,
            ResponseDueAt = dispute.ResponseDueAt
        }).ToArray();
        IReadOnlyList<RegistrationMaterialChangeChoiceDto> materialChangeChoiceDtos = (await materialChangeChoices.GetByPaymentAsync(
            order.TenantId, row.Value.Attempt.Id, cancellationToken)).Select(MapMaterialChangeChoice).ToArray();
        long refundedAmountMinor = refundAttempts
            .Where(attempt => attempt.BuyerRefundSucceededAt.HasValue)
            .Sum(attempt => attempt.Allocation.TotalMinor);
        long refundPendingAmountMinor = refundAttempts
            .Where(attempt => attempt.ReservesCapacity && !attempt.BuyerRefundSucceededAt.HasValue)
            .Sum(attempt => attempt.Allocation.TotalMinor);
        bool providerProvenCapture = row.Value.Attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded &&
            row.Value.Attempt.HasImmutableAcceptance &&
            !string.IsNullOrWhiteSpace(row.Value.Attempt.ProviderPaymentId);
        if (!providerProvenCapture)
        {
            materialChangeChoiceDtos = [];
        }
        bool refundCapacityAvailable = providerProvenCapture &&
            checked(refundedAmountMinor + refundPendingAmountMinor) < row.Value.Attempt.TotalMinor;
        bool refundBlockedByDispute = disputes.Any(dispute => dispute.IsOpen);
        return result with
        {
            RefundedAmountMinor = refundedAmountMinor,
            RefundPendingAmountMinor = refundPendingAmountMinor,
            Refunds = refundDtos,
            Disputes = disputeDtos,
            MaterialChangeChoices = materialChangeChoiceDtos,
            BuyerRefundRequestAvailable = buyerRefundAllowed && refundCapacityAvailable && !refundBlockedByDispute,
            OrganizerRefundAvailable = organizerRefundAllowed && refundCapacityAvailable && !refundBlockedByDispute,
        };
    }

    public async Task<RegistrationPaymentCommandResultDto> RetryAsync(RegistrationOrder order, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (!IsCurrentlyPayable(order, now))
        {
            return Failure("payment_retry_not_available", "Payment retry is not available.");
        }

        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? row = await attempts.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        if (row is null)
        {
            return Failure("payment_retry_not_available", "Payment retry is not available.");
        }

        if (!await acceptanceFreshness.IsCurrentAsync(row.Value.Attempt, cancellationToken))
        {
            return Failure("payment_acceptance_stale", "Current buyer acceptance evidence is required before retry.");
        }

        if (IsAlreadyQueuedAfterUserRetry(row.Value.Attempt, row.Value.DispatchEffect))
        {
            return Success(Map(order, row.Value.Attempt, row.Value.DispatchEffect));
        }

        PaymentAttemptStatusEnum status = (PaymentAttemptStatusEnum)row.Value.Attempt.PaymentAttemptStatusId;
        if (status is PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled)
        {
            RegistrationPaymentAttemptClaimResult terminalRetry = await claims.ClaimAsync(
                new(order.TenantId, order.Id, now, row.Value.Attempt.Id, row.Value.Attempt.AcceptanceSnapshot), cancellationToken);
            return terminalRetry.Success && terminalRetry.Attempt is not null && terminalRetry.DispatchEffect is not null
                ? Success(Map(order, terminalRetry.Attempt, terminalRetry.DispatchEffect))
                : Failure(terminalRetry.FailureCode ?? "payment_retry_not_available", terminalRetry.Message);
        }

        if (!CanRetry(order, row.Value.Attempt, row.Value.DispatchEffect, now))
        {
            return Failure("payment_retry_not_available", "Payment retry is not available.");
        }

        DateTime requestedAt = now;
        if (!await attempts.RetryParkedPreHandoffAsync(order.TenantId, row.Value.Attempt.Id, requestedAt, cancellationToken))
        {
            return Failure("payment_retry_not_available", "Payment retry is not available.");
        }

        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? refreshed = await attempts.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        return refreshed is null
            ? Failure("payment_retry_not_available", "Payment retry is not available.")
            : Success(Map(order, refreshed.Value.Attempt, refreshed.Value.DispatchEffect));
    }

    public async Task<RegistrationPaymentCheckoutTargetDto?> ResolveCheckoutTargetAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (!IsCurrentlyPayable(order, now))
        {
            return null;
        }

        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? row = await attempts.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        PaymentAttempt attempt = row?.Attempt!;
        if (attempt is null || !attempt.HasImmutableAcceptance ||
            (PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId != PaymentAttemptStatusEnum.RequiresAction ||
            attempt.ProviderCheckoutSessionId is not { } sessionId)
        {
            return null;
        }

        HostedCheckoutRetrieveResult result = await checkoutRetriever.RetrieveAsync(
            HostedCheckoutRetrieveRequest.Create(attempt.ProviderCode, attempt.RecipientSnapshot.ExternalAccountId, sessionId),
            cancellationToken);
        if (result is not { Outcome: HostedCheckoutOperationOutcome.Succeeded, Session: { } session } ||
            !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal) ||
            session.Status != HostedCheckoutSessionStatus.Open ||
            session.PaymentStatus != HostedCheckoutPaymentStatus.Unpaid ||
            session.ExpiresAt is not { } expiresAt || expiresAt <= now ||
            session.AmountTotalMinor != attempt.TotalMinor ||
            !string.Equals(session.CurrencyCode, attempt.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
            session.HostedUrl is not { IsAbsoluteUri: true } url)
        {
            return null;
        }

        return new RegistrationPaymentCheckoutTargetDto { Url = url.AbsoluteUri };
    }

    private RegistrationPaymentDto Map(
        RegistrationOrder order,
        PaymentAttempt attempt,
        CheckoutDispatchEffect dispatch,
        bool? retryAvailable = null,
        string? failureCode = null,
        bool needsReconciliation = false)
    {
        string statusCode = needsReconciliation ? "NeedsReconciliation" : StatusCode(order, attempt, dispatch);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        bool currentlyPayable = IsCurrentlyPayable(order, now);
        return new RegistrationPaymentDto
        {
            Id = attempt.Id,
            RegistrationOrderId = order.Id,
            StatusCode = statusCode,
            StatusName = statusCode switch
            {
                "RequiresAction" => "Requires action",
                "NeedsReconciliation" => "Needs reconciliation",
                _ => statusCode
            },
            HostedRedirectAvailable = currentlyPayable && statusCode == "RequiresAction" && attempt.ProviderCheckoutSessionId is not null,
            RetryAvailable = currentlyPayable && (retryAvailable ?? CanRetry(order, attempt, dispatch, now)),
            FailureCode = failureCode ?? (statusCode == "Failed" ? dispatch.LastFailureCode : null),
            CreatedAt = attempt.CreatedAt,
            LastUpdatedAt = attempt.UpdatedAt ?? attempt.LastStatusObservedAt,
            ExpiresAt = attempt.ExpiresAt,
            CapturedAmountMinor = attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded
                ? attempt.TotalMinor
                : 0,
            CurrencyCode = attempt.CurrencyCode,
            CurrencyMinorUnitDigits = Explore.Domain.ValueObjects.CurrencyMetadata.Get(attempt.CurrencyCode).MinorUnitDigits
        };
    }

    private static string StatusCode(RegistrationOrder order, PaymentAttempt attempt, CheckoutDispatchEffect dispatch)
    {
        if ((RegistrationOrderStatusEnum)order.RegistrationOrderStatusId == RegistrationOrderStatusEnum.NeedsReconciliation)
        {
            return "NeedsReconciliation";
        }

        return (PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId switch
        {
            PaymentAttemptStatusEnum.Created => "Created",
            PaymentAttemptStatusEnum.DispatchPending when dispatch.Status == OutboxMessageStatus.DeadLettered => "Failed",
            PaymentAttemptStatusEnum.DispatchPending => "Processing",
            PaymentAttemptStatusEnum.RequiresAction => "RequiresAction",
            PaymentAttemptStatusEnum.Processing => "Processing",
            PaymentAttemptStatusEnum.Unknown => "Unknown",
            PaymentAttemptStatusEnum.Failed => "Failed",
            PaymentAttemptStatusEnum.Cancelled => "Cancelled",
            PaymentAttemptStatusEnum.Succeeded => "Succeeded",
            _ => "Unknown"
        };
    }

    private static bool CanRetry(RegistrationOrder order, PaymentAttempt attempt, CheckoutDispatchEffect dispatch, DateTime now) =>
        IsCurrentlyPayable(order, now) &&
        ((PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId is PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled ||
         ((PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId == PaymentAttemptStatusEnum.DispatchPending &&
          attempt.ProviderCheckoutSessionId is null && dispatch.Status == OutboxMessageStatus.DeadLettered));

    private static bool IsAlreadyQueuedAfterUserRetry(PaymentAttempt attempt, CheckoutDispatchEffect dispatch) =>
        (((PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId == PaymentAttemptStatusEnum.Created && dispatch.Status == OutboxMessageStatus.Pending) ||
         ((PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId == PaymentAttemptStatusEnum.DispatchPending && dispatch.Status == OutboxMessageStatus.Failed)) &&
        attempt.ProviderCheckoutSessionId is null &&
        dispatch.ParkedAt is null &&
        dispatch.LastFailureCode is null;

    private static bool IsCurrentlyPayable(RegistrationOrder order, DateTime now) =>
        RegistrationPaymentPayability.IsCurrentlyPayable(
            order.RegistrationOrderStatusId,
            order.TotalDueMinorSnapshot,
            order.ExpiresAt,
            now);

    private static RegistrationPaymentCommandResultDto Success(RegistrationPaymentDto payment) =>
        RegistrationPaymentCommandResultDto.Success(Guid.Empty, null, payment);

    private static RegistrationPaymentCommandResultDto Failure(string code, string? message) =>
        RegistrationPaymentCommandResultDto.Failure(BaseCommandResponse.Failure<Guid>(code, message));

    internal static RegistrationRefundDto MapRefund(RefundAttempt attempt)
    {
        bool buyerRefunded = attempt.BuyerRefundSucceededAt.HasValue;
        string code = buyerRefunded ? nameof(RefundAttemptStatusEnum.Succeeded) : attempt.Status.ToString();
        return new()
        {
            Id = attempt.Id,
            StatusCode = code,
            StatusName = code switch
            {
                nameof(RefundAttemptStatusEnum.DispatchPending) => "Processing",
                nameof(RefundAttemptStatusEnum.RequiresAction) => "Requires action",
                nameof(RefundAttemptStatusEnum.Succeeded) => "Refunded",
                _ => code
            },
            FailureCode = buyerRefunded ? null : attempt.FailureCode,
            SettlementRetryAvailable = attempt.SourceCampaignId is null &&
                                       attempt.Status == RefundAttemptStatusEnum.RequiresAction &&
                                       attempt.FailureCode is not null,
            AmountMinor = attempt.Allocation.TotalMinor,
            CurrencyCode = attempt.CurrencyCode,
            AcceptedRefundPolicyVersion = attempt.RefundPolicyVersion,
            CreatedAt = attempt.CreatedAt,
            LastObservedAt = attempt.LastObservedAt,
            SucceededAt = attempt.BuyerRefundSucceededAt
        };
    }

    internal static RegistrationMaterialChangeChoiceDto MapMaterialChangeChoice(
        RegistrationMaterialChangeChoice choice) => new()
        {
            Id = choice.Id,
            CampaignId = choice.RefundCampaignId,
            StatusCode = choice.Status.ToString(),
            CreatedAt = choice.CreatedAt,
            DecidedAt = choice.DecidedAt
        };
}
