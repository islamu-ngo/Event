using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RefundCampaignPaymentPage(IReadOnlyList<PaymentAttempt> Payments, bool HasMore);

public interface IRefundCampaignRepository
{
    Task<RefundCampaign> CreateAsync(
        RefundCampaign campaign,
        OutboxMessage processTrigger,
        CancellationToken cancellationToken);

    Task<RefundCampaign?> GetByIdAsync(Guid tenantId, Guid campaignId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundCampaign>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<bool> ResumeAsync(
        Guid tenantId,
        Guid campaignId,
        OutboxMessage processTrigger,
        DateTime requestedAt,
        CancellationToken cancellationToken);

    Task<(RefundCampaign Campaign, RefundCampaignClaim Claim)?> TryClaimAsync(
        Guid tenantId,
        Guid campaignId,
        Guid ownerId,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<RefundCampaignPaymentPage> GetCapturedPaymentPageAsync(
        RefundCampaign campaign,
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> CompleteBatchAsync(
        Guid tenantId,
        Guid campaignId,
        RefundCampaignClaim claim,
        long? cursor,
        RefundCampaignBatchOutcome outcome,
        bool hasMore,
        IReadOnlyCollection<RegistrationMaterialChangeChoice> materialChangeChoices,
        IReadOnlyCollection<OutboxMessage> outboxMessages,
        DateTime completedAt,
        CancellationToken cancellationToken);

    Task RefreshOutcomeCountersAsync(
        Guid tenantId,
        Guid campaignId,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task RequireOperatorAsync(
        Guid tenantId,
        Guid campaignId,
        DateTime observedAt,
        CancellationToken cancellationToken);
}
