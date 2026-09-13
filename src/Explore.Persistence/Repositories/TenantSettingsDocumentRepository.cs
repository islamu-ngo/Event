namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

public sealed class TenantSettingsDocumentRepository : GenericRepository<TenantSettingsDocument, Guid>, ITenantSettingsDocumentRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantSettingsDocumentRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantSettingsDocument> CreateIfMissingAsync(
        TenantSettingsDocument document,
        CancellationToken cancellationToken = default)
    {
        const string savepoint = "tenant_document_provision";
        var transaction = _dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        }

        try
        {
            await _dbContext.TenantSettingsDocuments.AddAsync(document, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return document;
        }
        catch (Exception exception)
        {
            // Cleanup must complete even when the operation was cancelled. Never roll back the
            // caller's transaction or clear its other tracked changes to recover this insert.
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            _dbContext.Entry(document).State = EntityState.Detached;

            if (exception is DbUpdateException updateException
                && RegistrationUniqueConflictClassifier.IsExpectedConflict(updateException,
                    [RelationalConstraintDescriptorResolver.UniqueIndex<TenantSettingsDocument>(
                        _dbContext, nameof(TenantSettingsDocument.TenantId), nameof(TenantSettingsDocument.DocumentKey))]))
            {
                var winner = await GetTrackedByTenantAndDocumentKey(
                    document.TenantId, document.DocumentKey, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }
            }

            // An isolation snapshot may not see the winner. Its owner must retry the whole
            // transaction; unrelated constraints, provider errors and cancellation also escape.
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
        }
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
