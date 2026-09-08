using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationMaterialChangeChoiceService(
    IRegistrationMaterialChangeChoiceRepository choices,
    IRefundCampaignRepository campaigns,
    IRegistrationPaymentAttemptRepository payments,
    IRefundAttemptRepository refunds,
    TimeProvider timeProvider)
{
    public async Task<RegistrationMaterialChangeChoiceCommandResultDto> RespondAsync(
        RegistrationOrder order,
        Guid campaignId,
        string choiceCode,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationMaterialChangeChoice? choice = await choices.GetAsync(
            order.TenantId, campaignId, order.Id, cancellationToken);
        RefundCampaign? campaign = await campaigns.GetByIdAsync(order.TenantId, campaignId, cancellationToken);
        if (choice is null || campaign is null || campaign.EventId != order.EventId ||
            campaign.Kind != RefundCampaignKind.MaterialChange || actorId == Guid.Empty)
        {
            return Failure("material_change_choice_not_found", "Material-change choice was not found.");
        }

        if (string.Equals(choiceCode, "accept_new_terms", StringComparison.Ordinal))
        {
            await choices.AcceptAsync(order.TenantId, choice.Id, actorId, now, cancellationToken);
            await campaigns.RefreshOutcomeCountersAsync(order.TenantId, campaign.Id, now, cancellationToken);
            RegistrationMaterialChangeChoice? accepted = await choices.GetAsync(
                order.TenantId, campaignId, order.Id, cancellationToken);
            if (accepted?.Status != MaterialChangeChoiceStatusEnum.AcceptedNewTerms)
            {
                return Failure("material_change_choice_invalid", "Material-change choice is invalid.");
            }
            return Success(accepted, null);
        }
        if (!string.Equals(choiceCode, "request_refund", StringComparison.Ordinal))
        {
            return Failure("material_change_choice_invalid", "Material-change choice is invalid.");
        }

        (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)? row = await payments.GetLatestByOrderAsync(
            order.TenantId, order.Id, cancellationToken);
        PaymentAttempt? payment = row?.Attempt;
        if (payment is null || payment.Id != choice.PaymentAttemptId || payment.AcceptanceSnapshot is null ||
            payment.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Succeeded ||
            string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            return Failure("refund_payment_not_captured", "Only the captured payment can be refunded.");
        }

        long capacity = await refunds.GetRefundableCapacityAsync(order.TenantId, payment.Id, cancellationToken);
        if (capacity <= 0)
        {
            return Failure("refund_capacity_exceeded", "No captured refund capacity remains.");
        }

        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(), order.TenantId, payment.Id, payment.AcceptanceSnapshot,
            payment.RecipientSnapshot.ExternalAccountId, payment.ProviderPaymentId,
            $"refund-material-change:{choice.Id:N}", capacity, now, campaign.Id, "buyer", "material_change");
        attempt.CreatedBy = actorId;
        RefundReservationResult reservation = await refunds.ReserveAndRecordMaterialChangeRefundAsync(
            attempt, choice.Id, actorId, now, RefundOutboxMessageFactory.CreateDispatch(attempt, now), cancellationToken);
        if (reservation.Disposition is not (RefundReservationDisposition.Reserved or RefundReservationDisposition.Duplicate) ||
            reservation.Attempt is null)
        {
            return Failure(
                reservation.Disposition switch
                {
                    RefundReservationDisposition.OpenDispute => "refund_open_dispute",
                    RefundReservationDisposition.CapacityExceeded => "refund_capacity_exceeded",
                    RefundReservationDisposition.MaterialChangeChoiceConflict => "material_change_choice_invalid",
                    _ => "refund_authority_mismatch"
                },
                "Refund choice could not be scheduled.");
        }

        await campaigns.RefreshOutcomeCountersAsync(order.TenantId, campaign.Id, now, cancellationToken);
        RegistrationMaterialChangeChoice recorded = (await choices.GetAsync(
            order.TenantId, campaignId, order.Id, cancellationToken))!;
        return Success(recorded, RegistrationPaymentContractService.MapRefund(reservation.Attempt));
    }

    private static RegistrationMaterialChangeChoiceCommandResultDto Success(
        RegistrationMaterialChangeChoice choice,
        RegistrationRefundDto? refund) => RegistrationMaterialChangeChoiceCommandResultDto.Success(
            choice.Id, null, RegistrationPaymentContractService.MapMaterialChangeChoice(choice), refund);

    private static RegistrationMaterialChangeChoiceCommandResultDto Failure(string code, string message) =>
        RegistrationMaterialChangeChoiceCommandResultDto.Failure(BaseCommandResponse.Failure<Guid>(code, message));
}
