using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Uses the shared EF Core context to save issuance evidence and keys without owning their transaction.
/// </summary>
/// <remarks>
/// The application caller owns serializable execution, authorization, commit and ambiguous-commit recovery.
/// Runtime reads retain the tenant filter and require an exact non-bypassed tenant lease.
/// Null-tenant issuance and recovery belong exclusively to the dedicated instance capability.
/// Recovery returns entities for metadata mapping, not credential replay.
/// </remarks>
/// <param name="dbContext">The context shared with the caller's issuance unit of work and authority fences.</param>
public sealed class ExternalApiKeyIssuanceReceiptRepository(ExploreDbContext dbContext)
    : IExternalApiKeyIssuanceReceiptRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Rejects both ambient System.Transactions ownership and an existing EF transaction, as well as
    /// any pending tracked change. It does not clear tracked state or open a transaction. For a
    /// tenant operation it also requires the exact active filter with no tenant-filter bypass.
    /// </remarks>
    public void RequireCleanWriteScope(Guid? tenantId)
    {
        if (System.Transactions.Transaction.Current is not null
            || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("API-key issuance requires its own clean transaction scope.");
        RequireTenantScope(tenantId);
    }

    private void RequireTenantScope(Guid? tenantId)
    {
        if (tenantId is not Guid tenant || tenant == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenant)
            throw new InvalidOperationException("API-key issuance requires the exact active tenant scope.");
    }

    private void RequireTransaction(Guid? tenantId)
    {
        RequireTenantScope(tenantId);
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("API-key issuance requires its caller-owned Serializable transaction.");
    }

    public Task<bool> ExistsByOwnerAndNameAsync(
        ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken cancellationToken)
    {
        RequireTransaction(dbContext.TenantFilterTenantId);
        return dbContext.ExternalApiKeys.AsNoTracking().AnyAsync(
            key => key.ExternalApiKeyOwnerTypeId == (int)ownerType && key.OwnerId == ownerId && key.Name == name,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses an untracked exact fingerprint-and-tenant lookup after the caller establishes
    /// current authority in its own transaction, including during commit-ambiguity recovery.
    /// </remarks>
    public Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint, Guid? tenantId, CancellationToken cancellationToken)
    {
        RequireTransaction(tenantId);
        return dbContext.ExternalApiKeyIssuanceReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.TenantId == tenantId
                && receipt.OperationFingerprint == operationFingerprint, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// First verifies the aggregate, tenant and owner match, then fences the key and its discovered
    /// status lookup before freshly checking usability and expiry against UTC time. Relational
    /// fences require the caller's active transaction and are held until it ends. All reads and
    /// fence operations receive the supplied cancellation token; no credential is reconstructed.
    /// </remarks>
    public async Task<ExternalApiKey?> GetIssuedKeyAsync(
        Guid keyId, Guid? tenantId, ExternalApiKeyOwnerType ownerType, Guid ownerId,
        CancellationToken cancellationToken)
    {
        RequireTransaction(tenantId);
        var ownedKeys = dbContext.ExternalApiKeys
            .AsNoTracking()
            .Where(key => key.Id == keyId && key.TenantId == tenantId
                && key.ExternalApiKeyOwnerTypeId == (int)ownerType && key.OwnerId == ownerId);
        if (!await ownedKeys.AnyAsync(cancellationToken))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(dbContext, keyId, cancellationToken);
        int? statusId = await ownedKeys.Select(key => (int?)key.ExternalApiKeyStatusId)
            .SingleOrDefaultAsync(cancellationToken);
        if (statusId is not int status)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<ExternalApiKeyStatus>(
            dbContext, [status], cancellationToken);
        DateTime now = DateTime.UtcNow;
        return await ExternalApiKeyMetadataProjection.Apply(ownedKeys.Where(key =>
                key.ExternalApiKeyStatus.IsUsable && (key.ExpiresAt == null || key.ExpiresAt > now)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Adds both entities and calls SaveChangesAsync with the supplied token. SaveChanges flushes
    /// all pending context changes, so the caller must preserve the clean issuance write scope
    /// after its entry check. The caller's transaction is not committed here, and save completion
    /// does not resolve a later commit exception or authorize secret replay.
    /// </remarks>
    public async Task CreateAsync(
        ExternalApiKeyIssuanceReceipt receipt, ExternalApiKey key, CancellationToken cancellationToken)
    {
        RequireTransaction(key.TenantId);
        if (receipt.TenantId != key.TenantId || receipt.ExternalApiKeyId != key.Id
            || key.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin)
            throw new InvalidOperationException("API-key issuance evidence must match its tenant key.");
        dbContext.ExternalApiKeys.Add(key);
        dbContext.ExternalApiKeyIssuanceReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
