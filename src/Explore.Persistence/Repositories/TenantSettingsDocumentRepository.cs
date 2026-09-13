namespace Explore.Persistence.Repositories;

using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        Exception? insertFailure = null;
        TenantSettingsDocument result;
        try
        {
            try
            {
                await _dbContext.TenantSettingsDocuments.AddAsync(document, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                result = document;
            }
            catch (Exception exception)
            {
                insertFailure = exception;
                bool rollbackSucceeded = true;
                try
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                    }
                }
                catch (Exception rollbackFailure)
                {
                    rollbackSucceeded = false;
                    exception.Data["TenantSettingsDocument.SavepointRollbackFailure"] = rollbackFailure;
                }
                finally
                {
                    // A provider may have aborted the entire transaction and removed its
                    // savepoints. Always detach this candidate, never the owner's other work.
                    _dbContext.Entry(document).State = EntityState.Detached;
                }

                if (rollbackSucceeded && exception is DbUpdateException updateException
                    && RegistrationUniqueConflictClassifier.IsExpectedConflict(updateException,
                        [RelationalConstraintDescriptorResolver.UniqueIndex<TenantSettingsDocument>(
                            _dbContext, nameof(TenantSettingsDocument.TenantId), nameof(TenantSettingsDocument.DocumentKey))]))
                {
                    var winner = await GetTrackedByTenantAndDocumentKey(
                        document.TenantId, document.DocumentKey, cancellationToken);
                    if (winner is not null)
                    {
                        result = winner;
                    }
                    else
                    {
                        // The owner must retry a snapshot that cannot see the winner.
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }
        }
        catch (Exception failure)
        {
            await ReleaseSavepointAsync(transaction, savepoint, failure);
            throw;
        }

        await ReleaseSavepointAsync(transaction, savepoint, insertFailure);
        return result;
    }

    private static async Task ReleaseSavepointAsync(
        IDbContextTransaction? transaction, string savepoint, Exception? originalFailure)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
        }
        catch (Exception releaseFailure) when (originalFailure is not null)
        {
            originalFailure.Data["TenantSettingsDocument.SavepointReleaseFailure"] = releaseFailure;
            // Keep the original provider code/inner chain and cancellation token recognizable
            // to the transaction owner, even after an otherwise recoverable unique conflict.
            ExceptionDispatchInfo.Capture(originalFailure).Throw();
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
