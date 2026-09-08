using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationRefundService(
    IRegistrationPaymentAttemptRepository payments,
    IRefundAttemptRepository refunds,
    TimeProvider timeProvider)
{
    public async Task<RegistrationRefundCommandResultDto> InitiateAsync(
        RegistrationOrder order,
        long? requestedAmountMinor,
        string callerIdempotencyKey,
        Guid actorId,
        string authorityCode,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(callerIdempotencyKey))
        {
            return Failure("refund_request_invalid", "Refund request is invalid.");
        }

        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? row = await payments.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        PaymentAttempt? payment = row?.Attempt;
        if (payment is null || payment.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Succeeded ||
            payment.AcceptanceSnapshot is null || string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            return Failure("refund_payment_not_captured", "Only a captured payment can be refunded.");
        }

        long capacity = await refunds.GetRefundableCapacityAsync(order.TenantId, payment.Id, cancellationToken);
        long amount = requestedAmountMinor ?? capacity;
        if (amount <= 0 || amount > capacity)
        {
            return Failure("refund_capacity_exceeded", "Refund amount exceeds available captured capacity.");
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        string keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerIdempotencyKey))).ToLowerInvariant();
        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(), order.TenantId, payment.Id, payment.AcceptanceSnapshot,
            payment.RecipientSnapshot.ExternalAccountId, payment.ProviderPaymentId,
            $"refund:{payment.Id:N}:{keyHash}", amount, now, null, authorityCode, reasonCode);
        attempt.CreatedBy = actorId;
        RefundReservationResult reservation = await refunds.ReserveAndScheduleAsync(
            attempt, RefundOutboxMessageFactory.CreateDispatch(attempt, now), cancellationToken);
        return reservation.Disposition switch
        {
            RefundReservationDisposition.Reserved or RefundReservationDisposition.Duplicate when reservation.Attempt is not null =>
                RegistrationRefundCommandResultDto.Success(
                    reservation.Attempt.Id, null, RegistrationPaymentContractService.MapRefund(reservation.Attempt)),
            RefundReservationDisposition.OpenDispute => Failure(
                "refund_open_dispute", "Refund requires operator review while a dispute is open."),
            RefundReservationDisposition.CapacityExceeded => Failure(
                "refund_capacity_exceeded", "Refund amount exceeds available captured capacity."),
            _ => Failure("refund_authority_mismatch", "Refund authority could not be verified.")
        };
    }

    private static RegistrationRefundCommandResultDto Failure(string code, string message) =>
        RegistrationRefundCommandResultDto.Failure(BaseCommandResponse.Failure<Guid>(code, message));
}
