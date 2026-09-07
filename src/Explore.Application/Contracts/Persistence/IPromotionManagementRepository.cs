using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record PromotionManagementEntry(PromotionDefinition Definition, PromotionCode? ActiveCode);

public interface IPromotionManagementRepository
{
    Task<PromotionDefinition?> GetDefinitionForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid definitionId,
        CancellationToken cancellationToken);

    Task<PromotionManagementEntry?> GetManagementEntryAsync(
        Guid tenantId,
        Guid eventId,
        Guid definitionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PromotionManagementEntry>> ListManagementEntriesAsync(
        Guid tenantId,
        Guid eventId,
        Guid ticketCatalogVersionId,
        CancellationToken cancellationToken);

    Task AddDefinitionAsync(PromotionDefinition definition, CancellationToken cancellationToken);

    Task AddPublishedCodeAsync(
        PromotionCode code,
        PromotionCodeDigest digest,
        CancellationToken cancellationToken);

    Task ReplaceActiveCodeAsync(
        PromotionDefinition definition,
        PromotionCode code,
        PromotionCodeDigest digest,
        DateTime rotatedAtUtc,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
