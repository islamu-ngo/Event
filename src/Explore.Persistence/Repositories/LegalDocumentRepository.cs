namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class LegalDocumentRepository(ExploreDbContext dbContext)
    : ILegalDocumentRepository
{
    public Task<LegalDocument?> GetForUpdateAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken) =>
        AggregateQuery(tracking: true)
            .SingleOrDefaultAsync(
                document => document.AuthorityKey == AuthorityKey(scope, tenantId)
                    && document.Kind == kind,
                cancellationToken);

    public Task<LegalDocument?> GetByIdForUpdateAsync(
        Guid legalDocumentId,
        LegalDocumentScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(legalDocumentId, Guid.Empty);
        string authorityKey = AuthorityKey(scope, tenantId);
        return AggregateQuery(tracking: true)
            .SingleOrDefaultAsync(
                document => document.Id == legalDocumentId
                    && document.AuthorityKey == authorityKey,
                cancellationToken);
    }

    public Task<LegalDocument?> GetPublishedAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken) =>
        AggregateQuery(tracking: false)
            .SingleOrDefaultAsync(
                document => document.AuthorityKey == AuthorityKey(scope, tenantId)
                    && document.Kind == kind
                    && document.Publications.Any(publication =>
                        publication.LifecycleState
                            == LegalDocumentLifecycleState.Published),
                cancellationToken);

    public async Task AddAsync(
        LegalDocument legalDocument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legalDocument);
        await dbContext.LegalDocuments.AddAsync(legalDocument, cancellationToken);
    }

    private IQueryable<LegalDocument> AggregateQuery(bool tracking)
    {
        IQueryable<LegalDocument> query = dbContext.LegalDocuments
            .Include(document => document.Versions)
                .ThenInclude(version => version.Sources)
            .Include(document => document.Publications);
        return tracking ? query : query.AsNoTracking();
    }

    private static string AuthorityKey(
        LegalDocumentScope scope,
        Guid? tenantId) =>
        scope switch
        {
            LegalDocumentScope.Instance when tenantId is null => "instance",
            LegalDocumentScope.Tenant when tenantId is { } targetTenantId
                && targetTenantId != Guid.Empty => $"tenant:{targetTenantId:N}",
            _ => throw new ArgumentException(
                "Legal document target scope and tenant are inconsistent.",
                nameof(tenantId))
        };
}
