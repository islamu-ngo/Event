namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

public sealed class TenantSettingsDocumentRepository : GenericRepository<TenantSettingsDocument, Guid>, ITenantSettingsDocumentRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantSettingsDocumentRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantSettingsDocument?> GetByTenantAndDocumentKey(
        Guid tenantId,
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantSettingsDocuments
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                document => document.TenantId == tenantId && document.DocumentKey == documentKey,
                cancellationToken);
    }

    public async Task<TenantSettingsDocument?> GetTrackedByTenantAndDocumentKey(
        Guid tenantId,
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantSettingsDocuments
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .FirstOrDefaultAsync(
                document => document.TenantId == tenantId && document.DocumentKey == documentKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<TenantSettingsDocument>> GetManyForTenant(
        Guid tenantId,
        IEnumerable<string> documentKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = documentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (keys.Length == 0)
        {
            return [];
        }

        return await _dbContext.TenantSettingsDocuments
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(document => document.TenantId == tenantId && keys.Contains(document.DocumentKey))
            .ToListAsync(cancellationToken);
    }
}
