using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPromotionRedemptionRepository
{
    Task<IReadOnlyList<int>> GetDistinctLookupKeyVersionsAsync(
        Guid tenantId,
        Guid eventId,
        Guid ticketCatalogVersionId,
        CancellationToken cancellationToken);

    Task<PromotionCodeMatch?> GetCodeForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid ticketCatalogVersionId,
        IReadOnlyCollection<PromotionCodeDigest> candidateDigests,
        CancellationToken cancellationToken);

    Task<PromotionReservation?> GetActiveReservationForUpdateAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<int> GetTotalActiveOrConsumedCountAsync(
        Guid tenantId,
        Guid promotionDefinitionVersionId,
        CancellationToken cancellationToken);

    Task<int> GetVerifiedPurchaserActiveOrConsumedCountAsync(
        Guid tenantId,
        Guid promotionDefinitionVersionId,
        VerifiedPurchaserIdentity purchaser,
        CancellationToken cancellationToken);

    Task AddReservationAsync(PromotionReservation reservation, CancellationToken cancellationToken);
}

public sealed record PromotionCodeMatch(PromotionCode Code, PromotionDefinition Definition);
