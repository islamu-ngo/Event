using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class RefundCampaignProcessor(
    IRefundCampaignRepository campaigns,
    IRefundAttemptRepository refunds,
    IRegistrationMaterialChangeChoiceRepository materialChangeChoices,
    IRegistrationPaymentAttemptRepository payments,
    TimeProvider timeProvider)
{
    public const int BatchSize = 100;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<RefundCampaign?> ProcessBatchAsync(
        Guid tenantId,
        Guid campaignId,
        Guid workerId,
        CancellationToken cancellationToken)
    {
        DateTime claimedAt = timeProvider.GetUtcNow().UtcDateTime;
        (RefundCampaign Campaign, RefundCampaignClaim Claim)? owned = await campaigns.TryClaimAsync(
            tenantId, campaignId, workerId, claimedAt, LeaseDuration, cancellationToken);
        if (owned is null)
        {
            return await campaigns.GetByIdAsync(tenantId, campaignId, cancellationToken);
        }

        RefundCampaign campaign = owned.Value.Campaign;
        RefundCampaignPaymentPage page = await campaigns.GetCapturedPaymentPageAsync(
            campaign, BatchSize, cancellationToken);
        var dispatch = new List<OutboxMessage>(page.Payments.Count + (page.HasMore ? 1 : 0));
        var choices = new List<RegistrationMaterialChangeChoice>(page.Payments.Count);
        int generated = 0;
        int operatorCases = 0;
        DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;

        foreach (PaymentAttempt payment in page.Payments)
        {
            if (payment.PaymentAttemptStatusId is (int)Explore.Domain.Enums.PaymentAttemptStatusEnum.Failed or
                (int)Explore.Domain.Enums.PaymentAttemptStatusEnum.Cancelled)
            {
                continue;
            }

            if (campaign.Kind == Explore.Domain.Enums.RefundCampaignKind.EventCancellation &&
                payment.PaymentAttemptStatusId != (int)Explore.Domain.Enums.PaymentAttemptStatusEnum.Succeeded)
            {
                PaymentCancellationDisposition cancellation = await payments.TryCancelBeforeProviderHandoffAsync(
                    campaign.TenantId, payment.RegistrationOrderId, observedAt, cancellationToken);
                if (cancellation == PaymentCancellationDisposition.RequiresReconciliation)
                {
                    dispatch.Add(RefundOutboxMessageFactory.CreatePaymentCancellation(campaign, payment, observedAt));
                }
                continue;
            }

            if (payment.AcceptanceSnapshot is null)
            {
                operatorCases++;
                continue;
            }

            if (campaign.Kind == Explore.Domain.Enums.RefundCampaignKind.MaterialChange)
            {
                RegistrationMaterialChangeChoice? existing = await materialChangeChoices.GetAsync(
                    tenantId, campaign.Id, payment.RegistrationOrderId, cancellationToken);
                if (existing is null)
                {
                    choices.Add(RegistrationMaterialChangeChoice.Create(
                        Guid.CreateVersion7(), campaign, payment, observedAt));
                }
                generated++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
            {
                operatorCases++;
                continue;
            }

            long refundable = await refunds.GetRefundableCapacityAsync(
                tenantId, payment.Id, cancellationToken);
            if (refundable == 0)
            {
                continue;
            }

            RefundAttempt attempt = RefundAttempt.Create(
                Guid.CreateVersion7(), tenantId, payment.Id, payment.AcceptanceSnapshot,
                payment.RecipientSnapshot.ExternalAccountId, payment.ProviderPaymentId,
                $"refund-campaign:{campaign.Id:N}:{payment.Id:N}:{payment.AcceptanceSnapshot.Id:N}",
                refundable, observedAt, campaign.Id, "campaign", campaign.Kind.ToString());
            RefundReservationResult reservation = await refunds.ReserveAsync(attempt, cancellationToken);
            if (reservation.Disposition == RefundReservationDisposition.Reserved ||
                (reservation.Disposition == RefundReservationDisposition.Duplicate &&
                 reservation.Attempt?.SourceCampaignId == campaign.Id))
            {
                RefundAttempt scheduled = reservation.Attempt ?? attempt;
                generated++;
                if (reservation.Disposition == RefundReservationDisposition.Reserved)
                {
                    dispatch.Add(RefundOutboxMessageFactory.CreateDispatch(scheduled, observedAt));
                }
            }
            else if (reservation.Disposition is not RefundReservationDisposition.Duplicate)
            {
                operatorCases++;
            }
        }

        PaymentAttempt? last = page.Payments.LastOrDefault();
        if (page.HasMore)
        {
            dispatch.Add(RefundOutboxMessageFactory.CreateCampaignProcess(campaign, observedAt));
        }

        bool completed = await campaigns.CompleteBatchAsync(
            tenantId,
            campaign.Id,
            owned.Value.Claim,
            last?.CampaignCursor,
            new RefundCampaignBatchOutcome(page.Payments.Count, generated, operatorCases),
            page.HasMore,
            choices,
            dispatch,
            observedAt,
            cancellationToken);
        if (!completed)
        {
            return null;
        }

        await campaigns.RefreshOutcomeCountersAsync(tenantId, campaignId, observedAt, cancellationToken);
        return await campaigns.GetByIdAsync(tenantId, campaignId, cancellationToken);
    }
}
