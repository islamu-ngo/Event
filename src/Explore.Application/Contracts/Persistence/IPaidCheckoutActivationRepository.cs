using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPaidCheckoutActivationRepository
{
    Task<PaidCheckoutSaleControl?> GetSaleControlAsync(
        Guid tenantId,
        Guid? eventId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task AddSaleControlAsync(PaidCheckoutSaleControl control, CancellationToken cancellationToken);

    Task<PaidCheckoutReservedExposure> GetReservedExposureAsync(
        Guid tenantId,
        Guid eventId,
        Guid organizerActorId,
        string currencyCode,
        DateTime? rollingWindowStartsAt,
        Guid? excludedPaymentAttemptId,
        CancellationToken cancellationToken);

    Task<bool> HasPriorSucceededPaymentAsync(
        Guid tenantId,
        Guid organizerActorId,
        CancellationToken cancellationToken);

    Task<bool> HasApprovalAsync(
        Guid tenantId,
        Guid eventId,
        Guid organizerActorId,
        Guid policyVersionId,
        string currencyCode,
        PaidCheckoutReviewTrigger trigger,
        long orderAmountMinor,
        CancellationToken cancellationToken);

    Task<PaidCheckoutReviewApproval?> GetReviewAsync(
        Guid tenantId,
        Guid reviewId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task AddReviewAsync(PaidCheckoutReviewApproval review, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
