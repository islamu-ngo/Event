using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Services.Registration;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RefundCampaignRepository(ExploreDbContext dbContext) : IRefundCampaignRepository
{
    public async Task<RefundCampaign> CreateAsync(
        RefundCampaign campaign,
        OutboxMessage processTrigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(processTrigger);
        RefundCampaign? existing = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == campaign.TenantId && value.EventId == campaign.EventId &&
                     value.Kind == campaign.Kind && value.DecisionAt == campaign.DecisionAt,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await dbContext.RefundCampaigns.AddAsync(campaign, cancellationToken);
        await dbContext.OutboxMessages.AddAsync(processTrigger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    public Task<RefundCampaign?> GetByIdAsync(Guid tenantId, Guid campaignId, CancellationToken cancellationToken) =>
        dbContext.RefundCampaigns.AsNoTracking().SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);

    public async Task<IReadOnlyList<RefundCampaign>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        await dbContext.RefundCampaigns
            .AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.EventId == eventId)
            .OrderByDescending(value => value.DecisionAt)
            .ThenByDescending(value => value.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> ResumeAsync(
        Guid tenantId,
        Guid campaignId,
        OutboxMessage processTrigger,
        DateTime requestedAt,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);
        if (campaign is null || campaign.Status == RefundCampaignStatus.Completed)
        {
            return false;
        }
        campaign.Resume(requestedAt);
        List<RefundAttempt> providerBlocked = await dbContext.RefundAttempts
            .Where(value => value.TenantId == tenantId && value.SourceCampaignId == campaignId &&
                            value.Status == RefundAttemptStatusEnum.RequiresAction && value.FailureCode != null)
            .ToListAsync(cancellationToken);
        foreach (RefundAttempt attempt in providerBlocked)
        {
            attempt.RetryProviderBlocked(requestedAt);
            await dbContext.OutboxMessages.AddAsync(
                RefundOutboxMessageFactory.CreateReconciliation(attempt, requestedAt, requestedAt),
                cancellationToken);
        }
        await dbContext.OutboxMessages.AddAsync(processTrigger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(RefundCampaign Campaign, RefundCampaignClaim Claim)?> TryClaimAsync(
        Guid tenantId,
        Guid campaignId,
        Guid ownerId,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);
        if (campaign is null || campaign.Status is RefundCampaignStatus.Completed or RefundCampaignStatus.RequiresOperator ||
            campaign.ProcessingLeaseExpiresAt > claimedAt)
        {
            return null;
        }

        RefundCampaignClaim claim = campaign.Claim(ownerId, claimedAt, leaseDuration);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return (campaign, claim);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<RefundCampaignPaymentPage> GetCapturedPaymentPageAsync(
        RefundCampaign campaign,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (batchSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        IQueryable<PaymentAttempt> query =
            from payment in dbContext.PaymentAttempts
                .AsNoTracking()
                .Include(value => value.AcceptanceSnapshot)!
                .ThenInclude(value => value.Lines)
            join order in dbContext.RegistrationOrders.AsNoTracking()
                on new { payment.TenantId, payment.RegistrationOrderId }
                equals new { order.TenantId, RegistrationOrderId = order.Id }
            where payment.TenantId == campaign.TenantId && order.EventId == campaign.EventId &&
                  payment.CreatedAt <= campaign.DecisionAt &&
                  payment.CampaignCursor > campaign.Cursor
            orderby payment.CampaignCursor
            select payment;

        List<PaymentAttempt> rows = await query.Take(batchSize + 1).ToListAsync(cancellationToken);
        return new(rows.Take(batchSize).ToArray(), rows.Count > batchSize);
    }

    public async Task<bool> CompleteBatchAsync(
        Guid tenantId,
        Guid campaignId,
        RefundCampaignClaim claim,
        long? cursor,
        RefundCampaignBatchOutcome outcome,
        bool hasMore,
        IReadOnlyCollection<RegistrationMaterialChangeChoice> materialChangeChoices,
        IReadOnlyCollection<OutboxMessage> outboxMessages,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);
        if (campaign is null || campaign.ProcessingLeaseToken != claim.LeaseToken ||
            campaign.ProcessingFence != claim.ProcessingFence)
        {
            return false;
        }

        campaign.CompleteBatch(claim, cursor, outcome, hasMore, completedAt);
        await dbContext.RegistrationMaterialChangeChoices.AddRangeAsync(materialChangeChoices, cancellationToken);
        await dbContext.OutboxMessages.AddRangeAsync(outboxMessages, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task RefreshOutcomeCountersAsync(
        Guid tenantId,
        Guid campaignId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);
        if (campaign is null)
        {
            return;
        }

        int pending = await dbContext.RefundAttempts.CountAsync(
            value => value.TenantId == tenantId && value.SourceCampaignId == campaignId &&
                     (value.Status == RefundAttemptStatusEnum.Requested ||
                      value.Status == RefundAttemptStatusEnum.DispatchPending ||
                      value.Status == RefundAttemptStatusEnum.Pending ||
                      value.Status == RefundAttemptStatusEnum.RequiresAction), cancellationToken);
        int succeeded = await dbContext.RefundAttempts.CountAsync(
            value => value.TenantId == tenantId && value.SourceCampaignId == campaignId &&
                     value.Status == RefundAttemptStatusEnum.Succeeded, cancellationToken);
        int failed = await dbContext.RefundAttempts.CountAsync(
            value => value.TenantId == tenantId && value.SourceCampaignId == campaignId &&
                     (value.Status == RefundAttemptStatusEnum.Failed || value.Status == RefundAttemptStatusEnum.Cancelled),
            cancellationToken);
        int unknown = await dbContext.RefundAttempts.CountAsync(
            value => value.TenantId == tenantId && value.SourceCampaignId == campaignId &&
                     value.Status == RefundAttemptStatusEnum.Unknown, cancellationToken);
        int generated;
        if (campaign.Kind == RefundCampaignKind.MaterialChange)
        {
            generated = await dbContext.RegistrationMaterialChangeChoices.CountAsync(
                value => value.TenantId == tenantId && value.RefundCampaignId == campaignId,
                cancellationToken);
            pending += await dbContext.RegistrationMaterialChangeChoices.CountAsync(
                value => value.TenantId == tenantId && value.RefundCampaignId == campaignId &&
                         value.Status == MaterialChangeChoiceStatusEnum.Pending, cancellationToken);
            succeeded += await dbContext.RegistrationMaterialChangeChoices.CountAsync(
                value => value.TenantId == tenantId && value.RefundCampaignId == campaignId &&
                         value.Status == MaterialChangeChoiceStatusEnum.AcceptedNewTerms, cancellationToken);
            failed += await dbContext.RegistrationMaterialChangeChoices.CountAsync(
                value => value.TenantId == tenantId && value.RefundCampaignId == campaignId &&
                         value.Status == MaterialChangeChoiceStatusEnum.NotApplicable, cancellationToken);
        }
        else
        {
            generated = await dbContext.RefundAttempts.CountAsync(
                value => value.TenantId == tenantId && value.SourceCampaignId == campaignId,
                cancellationToken);
        }
        campaign.RefreshOutcomes(generated, pending, succeeded, failed, unknown, campaign.OperatorCaseCount, observedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RequireOperatorAsync(
        Guid tenantId,
        Guid campaignId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        RefundCampaign? campaign = await dbContext.RefundCampaigns.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == campaignId,
            cancellationToken);
        if (campaign is null)
        {
            return;
        }
        campaign.RequireOperator(observedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
