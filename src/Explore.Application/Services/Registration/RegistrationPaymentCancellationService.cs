using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationPaymentCancellationService(
    IRegistrationPaymentAttemptRepository payments,
    IRefundAttemptRepository refunds,
    IRefundCampaignRepository campaigns,
    IPaymentCancellationProvider provider,
    TimeProvider timeProvider)
{
    public async Task<bool> CancelAsync(
        Guid tenantId,
        Guid campaignId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await campaigns.GetByIdAsync(tenantId, campaignId, cancellationToken);
        PaymentAttempt? payment = await payments.GetByIdForCancellationAsync(
            tenantId, paymentAttemptId, cancellationToken);
        if (campaign is null || payment is null || campaign.Kind != RefundCampaignKind.EventCancellation)
        {
            return true;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (payment.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded)
        {
            return await ScheduleLateCaptureRefundAsync(campaign, payment, now, cancellationToken);
        }
        if (payment.PaymentAttemptStatusId is (int)PaymentAttemptStatusEnum.Failed or
            (int)PaymentAttemptStatusEnum.Cancelled)
        {
            return true;
        }
        if (payment.ProviderCheckoutSessionId is null && payment.ProviderPaymentId is null)
        {
            await campaigns.RequireOperatorAsync(tenantId, campaignId, now, cancellationToken);
            return true;
        }

        PaymentCancellationProviderResult result = await provider.CancelAsync(
            PaymentCancellationRequest.Create(
                payment.ProviderCode,
                payment.RecipientSnapshot.ExternalAccountId,
                payment.ProviderCheckoutSessionId,
                payment.ProviderPaymentId,
                $"cancel:{campaign.Id:N}:{payment.Id:N}"),
            cancellationToken);
        if (result.Outcome == PaymentCancellationProviderOutcome.Cancelled)
        {
            return await payments.MarkCancelledAfterProviderAsync(
                tenantId, payment.Id, campaign.CreatedBy!.Value, now,
                result.ProviderRequestId, cancellationToken);
        }
        if (result.Outcome == PaymentCancellationProviderOutcome.Failed)
        {
            await campaigns.RequireOperatorAsync(tenantId, campaignId, now, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> ScheduleLateCaptureRefundAsync(
        RefundCampaign campaign,
        PaymentAttempt payment,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (payment.AcceptanceSnapshot is null || payment.ProviderPaymentId is null)
        {
            await campaigns.RequireOperatorAsync(
                campaign.TenantId, campaign.Id, observedAt, cancellationToken);
            return true;
        }
        long capacity = await refunds.GetRefundableCapacityAsync(
            campaign.TenantId, payment.Id, cancellationToken);
        if (capacity == 0)
        {
            return true;
        }

        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(), campaign.TenantId, payment.Id, payment.AcceptanceSnapshot,
            payment.RecipientSnapshot.ExternalAccountId, payment.ProviderPaymentId,
            $"refund-campaign:{campaign.Id:N}:{payment.Id:N}:{payment.AcceptanceSnapshot.Id:N}",
            capacity, observedAt, campaign.Id, "campaign", "eventcancellation");
        attempt.CreatedBy = campaign.CreatedBy;
        RefundReservationResult reservation = await refunds.ReserveAndScheduleAsync(
            attempt, RefundOutboxMessageFactory.CreateDispatch(attempt, observedAt), cancellationToken);
        if (reservation.Disposition is RefundReservationDisposition.Reserved or RefundReservationDisposition.Duplicate)
        {
            await campaigns.RefreshOutcomeCountersAsync(
                campaign.TenantId, campaign.Id, observedAt, cancellationToken);
            return true;
        }

        await campaigns.RequireOperatorAsync(campaign.TenantId, campaign.Id, observedAt, cancellationToken);
        return true;
    }
}
