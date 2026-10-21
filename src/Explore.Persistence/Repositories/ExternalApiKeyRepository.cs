using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class ExternalApiKeyRepository : IExternalApiKeyRepository
{
    private readonly ExploreDbContext _dbContext;

    public ExternalApiKeyRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ExternalApiKey>> GetByOwner(ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        return await ExternalApiKeyMetadataProjection.Apply(_dbContext.ExternalApiKeys)
            .Where(apiKey => apiKey.ExternalApiKeyOwnerTypeId == (int)ownerType && apiKey.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ExternalApiKey>> GetByOwners(ExternalApiKeyOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        if (ownerIds.Count == 0)
        {
            return [];
        }

        var results = new List<ExternalApiKey>(ownerIds.Count);
        foreach (var chunk in ownerIds.Chunk(100))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkResults = await ExternalApiKeyMetadataProjection.Apply(_dbContext.ExternalApiKeys)
                .Where(apiKey => apiKey.ExternalApiKeyOwnerTypeId == (int)ownerType && chunk.Contains(apiKey.OwnerId))
                .ToListAsync(cancellationToken);
            results.AddRange(chunkResults);
        }
        return results;
    }

    public async Task<bool> ExistsByOwnerAndName(ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalApiKeys
            .AsNoTracking()
            .AnyAsync(apiKey => apiKey.ExternalApiKeyOwnerTypeId == (int)ownerType && apiKey.OwnerId == ownerId && apiKey.Name == name, cancellationToken);
    }

    public async Task<ExternalApiKey?> GetForTenantAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExternalApiKeyMetadataProjection.Apply(ManagementKeys())
            .FirstOrDefaultAsync(apiKey => apiKey.Id == id, cancellationToken);
    }

    public async Task<ExternalApiKey?> GetForManagementAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational()
            && _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Key management requires the caller's Serializable transaction.");
        if (_dbContext.Database.IsRelational()
            && await ManagementKeys().Where(key => key.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.Id, key => key.Id), cancellationToken) != 1)
            return null;
        return await GetForTenantAsync(id, cancellationToken);
    }

    /// <summary>Writes only management policy, revocation status and their audit metadata, never redacted private fields.</summary>
    public async Task Update(ExternalApiKey entity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        if (_dbContext.IsTenantFilterBypassed || _dbContext.TenantFilterTenantId is not { } tenantId
            || tenantId == Guid.Empty || entity.TenantId != tenantId)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        var owned = ManagementKeys().Where(key => key.Id == entity.Id && key.KeyId == entity.KeyId
            && key.OwnerId == entity.OwnerId && key.ExternalApiKeyOwnerTypeId == entity.ExternalApiKeyOwnerTypeId);
        if (_dbContext.Database.IsRelational())
        {
            if (_dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Key management requires the caller's Serializable transaction.");
            int changed = await owned.ExecuteUpdateAsync(setters => setters
                .SetProperty(key => key.Name, entity.Name)
                .SetProperty(key => key.Scopes, entity.Scopes)
                .SetProperty(key => key.ExpiresAt, entity.ExpiresAt)
                .SetProperty(key => key.ExternalApiKeyStatusId, entity.ExternalApiKeyStatusId)
                .SetProperty(key => key.UpdatedAt, entity.UpdatedAt)
                .SetProperty(key => key.UpdatedBy, entity.UpdatedBy), cancellationToken);
            if (changed != 1)
                throw new DbUpdateConcurrencyException("The original tenant key is no longer available for management.");
            return;
        }

        var current = await owned.SingleOrDefaultAsync(cancellationToken)
            ?? throw new DbUpdateConcurrencyException("The original tenant key is no longer available for management.");
        current.Name = entity.Name;
        current.Scopes = entity.Scopes;
        current.ExpiresAt = entity.ExpiresAt;
        current.ExternalApiKeyStatusId = entity.ExternalApiKeyStatusId;
        current.UpdatedAt = entity.UpdatedAt;
        current.UpdatedBy = entity.UpdatedBy;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<ExternalApiKey> ManagementKeys()
    {
        Guid? tenantId = _dbContext.TenantFilterTenantId;
        return _dbContext.ExternalApiKeys
            .Where(key => !_dbContext.IsTenantFilterBypassed && tenantId != null && key.TenantId == tenantId);
    }
}
