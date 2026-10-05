using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ExternalApiKeyIssuanceReceiptRepository(ExploreDbContext dbContext)
    : IExternalApiKeyIssuanceReceiptRepository
{
    public void RequireCleanWriteScope(Guid? tenantId)
    {
        if (System.Transactions.Transaction.Current is not null
            || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("API-key issuance requires its own clean transaction scope.");
        if (tenantId is Guid tenant && (tenant == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenant))
            throw new InvalidOperationException("API-key issuance requires the exact active tenant scope.");
    }

    public Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint, Guid? tenantId, CancellationToken cancellationToken) =>
        dbContext.ExternalApiKeyIssuanceReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
            .AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.TenantId == tenantId
                && receipt.OperationFingerprint == operationFingerprint, cancellationToken);

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

    public async Task CreateAsync(
        ExternalApiKeyIssuanceReceipt receipt, ExternalApiKey key, CancellationToken cancellationToken)
    {
        dbContext.ExternalApiKeys.Add(key);
        dbContext.ExternalApiKeyIssuanceReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
