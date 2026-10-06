using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Uses the shared EF Core context to save issuance evidence and keys without owning their transaction.
/// </summary>
/// <remarks>
/// The application caller owns serializable execution, authorization, commit and ambiguous-commit recovery.
/// Reads are untracked and bypass only the named tenant filter, replacing it with an exact nullable
/// tenant predicate; other filters remain in effect. Recovery returns entities for metadata mapping,
/// not credential replay.
/// </remarks>
/// <param name="dbContext">The context shared with the caller's issuance unit of work and authority fences.</param>
public sealed class ExternalApiKeyIssuanceReceiptRepository(ExploreDbContext dbContext)
    : IExternalApiKeyIssuanceReceiptRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Rejects both ambient System.Transactions ownership and an existing EF transaction, as well as
    /// any pending tracked change. It does not clear tracked state or open a transaction. For a
    /// nonnull tenant it also requires the exact active filter with no tenant-filter bypass.
    /// </remarks>
    public void RequireCleanWriteScope(Guid? tenantId)
    {
        if (System.Transactions.Transaction.Current is not null
            || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("API-key issuance requires its own clean transaction scope.");
        if (tenantId is Guid tenant && (tenant == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenant))
            throw new InvalidOperationException("API-key issuance requires the exact active tenant scope.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses an untracked exact fingerprint-and-tenant lookup, including an explicit null tenant
    /// for platform evidence. The named filter bypass does not grant authority; callers must
    /// establish current authority before this read, including during commit-ambiguity recovery.
    /// </remarks>
    public Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint, Guid? tenantId, CancellationToken cancellationToken) =>
        dbContext.ExternalApiKeyIssuanceReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
            .AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.TenantId == tenantId
                && receipt.OperationFingerprint == operationFingerprint, cancellationToken);

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
        var ownedKeys = dbContext.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
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
        return await ownedKeys.SingleOrDefaultAsync(key =>
            key.ExternalApiKeyStatus.IsUsable && (key.ExpiresAt == null || key.ExpiresAt > now),
            cancellationToken);
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
        dbContext.ExternalApiKeys.Add(key);
        dbContext.ExternalApiKeyIssuanceReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
