using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed partial class RegistrationOrderLifecycleService
{
    public Task<RegistrationOrderLifecycleResponseDto> FinalizePaidAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            FinalizeCoreAsync(orderId, tenantId, paid: true, cancellationToken),
            cancellationToken);

    public async Task<CheckoutDispatchConfigurationDisposition> CancelExpiredConfigurationBlockedPaymentAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            return CheckoutDispatchConfigurationDisposition.Stale;
        }

        Guid outboxMessageId = Guid.CreateVersion7();
        ConfigurationExpiryCancellationResult result = await unitOfWork.ExecuteSerializableAsync<ConfigurationExpiryCancellationResult>(async token =>
        {
            RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(
                claim.RegistrationOrderId,
                claim.TenantId,
                token);
            if (order is null)
            {
                return new(CheckoutDispatchConfigurationDisposition.Stale, false);
            }

            RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
            if (status == RegistrationOrderStatusEnum.Cancelled)
            {
                bool exactDuplicate = await paymentAttempts.CancelExpiredConfigurationBlockedAsync(
                    claim,
                    observedAt,
                    token);
                return new(
                    exactDuplicate
                        ? CheckoutDispatchConfigurationDisposition.CancelledExpired
                        : CheckoutDispatchConfigurationDisposition.Stale,
                    false);
            }

            if (!order.CanTransitionTo(RegistrationOrderStatusEnum.Cancelled) ||
                !await paymentAttempts.CancelExpiredConfigurationBlockedAsync(claim, observedAt, token))
            {
                return new(CheckoutDispatchConfigurationDisposition.Stale, false);
            }

            if (!await transitions.PersistAsync(
                    order.Id,
                    order.TenantId,
                    status,
                    RegistrationOrderStatusEnum.Cancelled,
                    observedAt,
                    token))
            {
                return new(CheckoutDispatchConfigurationDisposition.Stale, false);
            }

            await ReleaseActivePromotionAsync(order, observedAt, token);
            await LockActiveHoldCapacityPoolsAsync(order, token);
            await inventory.TryReleaseActiveHoldsForOrderAsync(
                order.Id,
                order.TenantId,
                RegistrationInventoryHoldStatusEnum.Cancelled,
                observedAt,
                token);
            await outbox.Create(RegistrationOrderOutboxMessageFactory.Create(
                outboxMessageId,
                order,
                RegistrationOrderStatusEnum.Cancelled,
                observedAt));
            await inventory.SaveChangesAsync(token);
            return new(CheckoutDispatchConfigurationDisposition.CancelledExpired, true);
        }, cancellationToken);

        if (result.WithdrawDeadline)
        {
            await deadlines.CancelAsync(
                ScheduledJobNames.InventoryHoldExpiry,
                InventoryHoldDeadline.KeyFor(claim.RegistrationOrderId),
                cancellationToken);
        }

        return result.Disposition;
    }

    private async Task<RegistrationOrderLifecycleResponseDto> WithdrawHoldDeadlineWhenTerminalAsync(
        Task<RegistrationOrderLifecycleResponseDto> transition,
        CancellationToken cancellationToken)
    {
        RegistrationOrderLifecycleResponseDto response = await transition;

        if (response.IsSuccess &&
            response.Order is not null &&
            RegistrationOrderRules.IsTerminal((RegistrationOrderStatusEnum)response.Order.StatusId))
        {
            await deadlines.CancelAsync(
                ScheduledJobNames.InventoryHoldExpiry,
                InventoryHoldDeadline.KeyFor(response.Id),
                cancellationToken);
        }

        return response;
    }

    private async Task<RegistrationOrderLifecycleResponseDto> CancelCoreAsync(Guid orderId, Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Guid outboxMessageId = Guid.CreateVersion7();
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
            if (order is null)
            {
                return Missing(orderId);
            }

            RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
            if (status == RegistrationOrderStatusEnum.Cancelled)
            {
                return Success(order, status, "Registration order is already cancelled.");
            }

            if (!order.CanTransitionTo(RegistrationOrderStatusEnum.Cancelled))
            {
                return Failure(order.Id, order, "Registration order cannot be cancelled from its current state.");
            }

            PaymentCancellationDisposition paymentCancellation = await paymentAttempts.TryCancelBeforeProviderHandoffAsync(
                tenantId, order.Id, now, token);
            if (paymentCancellation == PaymentCancellationDisposition.RequiresReconciliation)
            {
                if (status != RegistrationOrderStatusEnum.NeedsReconciliation)
                {
                    _ = await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        status,
                        RegistrationOrderStatusEnum.NeedsReconciliation,
                        now,
                        token);
                }

                return Failure(order.Id, order, "Payment reconciliation is required before cancellation.");
            }

            if (!await transitions.PersistAsync(order.Id, tenantId, status, RegistrationOrderStatusEnum.Cancelled, now, token))
            {
                return await CurrentOrConflictAsync(orderId, tenantId, "Registration order changed while it was cancelled.", token);
            }

            await ReleaseActivePromotionAsync(order, now, token);
            await LockActiveHoldCapacityPoolsAsync(order, token);
            await inventory.TryReleaseActiveHoldsForOrderAsync(
                order.Id,
                tenantId,
                RegistrationInventoryHoldStatusEnum.Cancelled,
                now,
                token);
            await outbox.Create(RegistrationOrderOutboxMessageFactory.Create(
                outboxMessageId, order, RegistrationOrderStatusEnum.Cancelled, now));
            await inventory.SaveChangesAsync(token);
            return Success(order, RegistrationOrderStatusEnum.Cancelled, "Registration order cancelled.");
        }, cancellationToken);
    }

    private async Task<PaymentEvidenceState> GetPaymentEvidenceStateAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        SucceededPaymentLookupResult evidence =
            await finalization.GetSucceededPaymentAsync(order.TenantId, order.Id, cancellationToken);
        if (evidence.Status == SucceededPaymentLookupStatus.Conflict)
        {
            return PaymentEvidenceState.Duplicate;
        }

        if (evidence.Status != SucceededPaymentLookupStatus.Found ||
            evidence.Attempt is not { } attempt || evidence.Observation is not { } observation)
        {
            return PaymentEvidenceState.Missing;
        }

        bool exact = attempt.TenantId == order.TenantId &&
               attempt.RegistrationOrderId == order.Id &&
               attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded &&
               string.Equals(attempt.CurrencyCode, order.CurrencyCode, StringComparison.Ordinal) &&
               attempt.OrganizerAmountMinor == order.OrganizerDirectedTotalMinorSnapshot &&
               attempt.PlatformFeeMinor == order.PlatformFeeTotalMinorSnapshot &&
               attempt.PlatformContributionMinor == order.PlatformContributionTotalMinorSnapshot &&
               attempt.TotalMinor == order.TotalDueMinorSnapshot &&
               observation.TenantId == order.TenantId &&
               observation.RegistrationOrderId == order.Id &&
               observation.PaymentAttemptId == attempt.Id &&
               string.Equals(observation.ProviderCheckoutSessionId, attempt.ProviderCheckoutSessionId, StringComparison.Ordinal) &&
               string.Equals(observation.ProviderPaymentId, attempt.ProviderPaymentId, StringComparison.Ordinal);
        return exact ? PaymentEvidenceState.Exact : PaymentEvidenceState.Mismatch;
    }

    private Task<RegistrationOrderLifecycleResponseDto> ParkPaidIssueAsync(
        RegistrationOrder initialOrder,
        DateTime now,
        string code,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(async token =>
        {
            RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(
                initialOrder.Id, initialOrder.TenantId, token);
            if (order is null)
            {
                return Missing(initialOrder.Id);
            }

            RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
            if (status == RegistrationOrderStatusEnum.AwaitingPayment &&
                !await transitions.PersistAsync(
                    order.Id,
                    order.TenantId,
                    RegistrationOrderStatusEnum.AwaitingPayment,
                    RegistrationOrderStatusEnum.NeedsReconciliation,
                    now,
                    token))
            {
                throw new LifecycleRaceException();
            }

            await inventory.SaveChangesAsync(token);
            return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation, code);
        }, cancellationToken);

    private sealed record ConfigurationExpiryCancellationResult(
        CheckoutDispatchConfigurationDisposition Disposition,
        bool WithdrawDeadline);

    private enum PaymentEvidenceState
    {
        Missing,
        Exact,
        Mismatch,
        Duplicate
    }
}
