using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class OrganizationTenantEvidenceRepository(ExploreDbContext dbContext)
    : GenericRepository<OrganizationTenantEvidence, Guid>(dbContext),
        IOrganizationTenantEvidenceRepository
{
    public Task<OrganizationTenantEvidence?> GetDetailsAsync(
        Guid id,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges)
            .FirstOrDefaultAsync(evidence => evidence.Id == id, cancellationToken);
    }

    public Task<OrganizationTenantEvidence?> GetByDocumentAsync(
        Guid organizationTenantId,
        Guid documentStorageObjectId,
        CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges: false)
            .FirstOrDefaultAsync(
                evidence => evidence.OrganizationTenantId == organizationTenantId
                    && evidence.DocumentStorageObjectId == documentStorageObjectId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationTenantEvidence>> ListByParticipationAsync(
        Guid organizationTenantId,
        CancellationToken cancellationToken)
    {
        return await DetailsQuery(trackChanges: false)
            .Where(evidence => evidence.OrganizationTenantId == organizationTenantId)
            .OrderByDescending(evidence => evidence.CreatedAt)
            .ThenBy(evidence => evidence.Id)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<OrganizationTenantEvidence> DetailsQuery(bool trackChanges)
    {
        IQueryable<OrganizationTenantEvidence> query = dbContext.OrganizationTenantEvidence
            .Include(evidence => evidence.OrganizationTenant)
                .ThenInclude(participation => participation!.Organization)
            .Include(evidence => evidence.DocumentStorageObject)
                .ThenInclude(storageObject => storageObject!.FileType)
            .Include(evidence => evidence.ReviewStatus);

        return trackChanges ? query : query.AsNoTracking();
    }
}
