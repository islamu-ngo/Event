using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PaidCheckoutActivationRepository(ExploreDbContext dbContext) : IPaidCheckoutActivationRepository
{
    public Task<PaidCheckoutSaleControl?> GetSaleControlAsync(
        Guid tenantId,
        Guid? eventId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<PaidCheckoutSaleControl> query = dbContext.PaidCheckoutSaleControls
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(value => value.TenantId == tenantId && value.EventId == eventId);
        return (forUpdate ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddSaleControlAsync(PaidCheckoutSaleControl control, CancellationToken cancellationToken) =>
        await dbContext.PaidCheckoutSaleControls.AddAsync(control, cancellationToken);

    public async Task<PaidCheckoutReservedExposure> GetReservedExposureAsync(
        Guid tenantId,
        Guid eventId,
        Guid organizerActorId,
        string currencyCode,
        DateTime? rollingWindowStartsAt,
        Guid? excludedPaymentAttemptId,
        CancellationToken cancellationToken)
    {
        int failed = (int)PaymentAttemptStatusEnum.Failed;
        int cancelled = (int)PaymentAttemptStatusEnum.Cancelled;
        var rows = from attempt in dbContext.PaymentAttempts
                       .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                       .AsNoTracking()
                   join order in dbContext.RegistrationOrders
                           .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                           .AsNoTracking()
                       on new { attempt.TenantId, Id = attempt.RegistrationOrderId }
                       equals new { order.TenantId, order.Id }
                   where attempt.TenantId == tenantId &&
                         attempt.RecipientSnapshot.OrganizerActorId == organizerActorId &&
                         attempt.CurrencyCode == currencyCode &&
                         attempt.Id != excludedPaymentAttemptId &&
                         attempt.PaymentAttemptStatusId != failed &&
                         attempt.PaymentAttemptStatusId != cancelled
                   select new
                   {
                       attempt.TotalMinor,
                       IsEvent = order.EventId == eventId,
                       IsRolling = rollingWindowStartsAt == null || attempt.CreatedAt >= rollingWindowStartsAt
                   };
        var exposure = await rows.GroupBy(_ => 1).Select(group => new
        {
            EventAmount = group.Sum(value => value.IsEvent ? value.TotalMinor : 0L),
            EventCount = group.Sum(value => value.IsEvent ? 1 : 0),
            RollingAmount = group.Sum(value => value.IsRolling ? value.TotalMinor : 0L),
            RollingCount = group.Sum(value => value.IsRolling ? 1 : 0)
        }).SingleOrDefaultAsync(cancellationToken);
        return exposure is null
            ? new(currencyCode, 0, 0, 0, 0)
            : new(currencyCode, exposure.EventAmount, exposure.EventCount, exposure.RollingAmount, exposure.RollingCount);
    }

    public Task<bool> HasPriorSucceededPaymentAsync(
        Guid tenantId,
        Guid organizerActorId,
        CancellationToken cancellationToken) => dbContext.PaymentAttempts
        .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
        .AsNoTracking()
        .AnyAsync(value =>
            value.TenantId == tenantId &&
            value.RecipientSnapshot.OrganizerActorId == organizerActorId &&
            value.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded,
            cancellationToken);

    public Task<bool> HasApprovalAsync(
        Guid tenantId,
        Guid eventId,
        Guid organizerActorId,
        Guid policyVersionId,
        string currencyCode,
        PaidCheckoutReviewTrigger trigger,
        long orderAmountMinor,
        CancellationToken cancellationToken) => dbContext.PaidCheckoutReviewApprovals
        .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
        .AsNoTracking()
        .AnyAsync(value =>
            value.TenantId == tenantId && value.EventId == eventId &&
            value.OrganizerActorId == organizerActorId && value.PaidEventPolicyVersionId == policyVersionId &&
            value.CurrencyCode == currencyCode && value.TriggerId == (int)trigger && value.StatusCode == "approved" &&
            (value.MaximumOrderAmountMinor == null || value.MaximumOrderAmountMinor >= orderAmountMinor), cancellationToken);

    public Task<PaidCheckoutReviewApproval?> GetReviewAsync(
        Guid tenantId,
        Guid reviewId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<PaidCheckoutReviewApproval> query = dbContext.PaidCheckoutReviewApprovals
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(value => value.TenantId == tenantId && value.Id == reviewId);
        return (forUpdate ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddReviewAsync(PaidCheckoutReviewApproval review, CancellationToken cancellationToken) =>
        await dbContext.PaidCheckoutReviewApprovals.AddAsync(review, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
