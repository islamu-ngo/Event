using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class RefundReconciliationHealthQuery
{
    internal static async Task<RefundReconciliationHealth> ReadAsync(
        ExploreDbContext context, DateTime observedAt, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await PostgresRefundReconciliationHealthContract.ObserveAsync(context, observedAt, cancellationToken);

        // This bypass belongs only to the separately acquired Global context.
        // Portable providers retain the same status and caller-clock semantics.
        IQueryable<RefundAttempt> attempts = context.RefundAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        IQueryable<RefundCampaign> campaigns = context.RefundCampaigns
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        IQueryable<PaymentDispute> disputes = context.PaymentDisputes
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        int pending = await attempts.CountAsync(value =>
            value.Status == RefundAttemptStatusEnum.Requested ||
            value.Status == RefundAttemptStatusEnum.DispatchPending ||
            value.Status == RefundAttemptStatusEnum.Pending, cancellationToken);
        int unknown = await attempts.CountAsync(value => value.Status == RefundAttemptStatusEnum.Unknown, cancellationToken);
        int requiresAction = await attempts.CountAsync(
            value => value.Status == RefundAttemptStatusEnum.RequiresAction, cancellationToken);
        DateTime recentFailureCutoff = observedAt.AddHours(-24);
        int failed = await attempts.CountAsync(value => value.Status == RefundAttemptStatusEnum.Failed &&
            (value.UpdatedAt ?? value.LastObservedAt) >= recentFailureCutoff, cancellationToken);
        int operatorCampaigns = await campaigns.CountAsync(
            value => value.Status == RefundCampaignStatus.RequiresOperator, cancellationToken);
        DateTime dueSoonCutoff = observedAt.AddHours(24);
        int disputesOverdue = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
            value.ResponseDueAt < observedAt, cancellationToken);
        int disputesDueSoon = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
            value.ResponseDueAt >= observedAt && value.ResponseDueAt <= dueSoonCutoff, cancellationToken);
        DateTime urgentCutoff = observedAt.AddHours(72);
        int disputesDueWithin72Hours = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
            value.ResponseDueAt > dueSoonCutoff && value.ResponseDueAt <= urgentCutoff, cancellationToken);
        DateTime? oldest = await attempts
            .Where(value => value.Status == RefundAttemptStatusEnum.Requested ||
                value.Status == RefundAttemptStatusEnum.DispatchPending ||
                value.Status == RefundAttemptStatusEnum.Pending ||
                value.Status == RefundAttemptStatusEnum.RequiresAction ||
                value.Status == RefundAttemptStatusEnum.Unknown)
            .MinAsync(value => (DateTime?)value.LastObservedAt, cancellationToken);
        return new(pending, unknown, requiresAction, failed, operatorCampaigns,
            disputesDueSoon, disputesDueWithin72Hours, disputesOverdue, oldest);
    }
}
