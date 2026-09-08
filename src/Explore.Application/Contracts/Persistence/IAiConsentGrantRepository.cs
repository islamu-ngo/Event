using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IAiConsentGrantRepository : IGenericRepository<AiConsentGrant, Guid>
{
    Task<AiConsentGrant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<AiConsentGrant?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiConsentGrant>> ListForUserAsync(Guid subjectUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiConsentGrant>> ListGrantedForSubjectAsync(
        Guid subjectUserId,
        string entityName,
        string fieldName,
        int providerTrustTierId,
        CancellationToken cancellationToken);
    Task<AiConsentGrant?> FindActiveGrantAsync(
        Guid subjectUserId,
        string entityName,
        string fieldName,
        int providerTrustTierId,
        CancellationToken cancellationToken);
    Task AddAsync(AiConsentGrant grant, CancellationToken cancellationToken);
    Task UpdateAsync(AiConsentGrant grant, CancellationToken cancellationToken);
    Task RevokeAsync(Guid grantId, Guid revokedByUserId, DateTime revokedAtUtc, CancellationToken cancellationToken);
}
