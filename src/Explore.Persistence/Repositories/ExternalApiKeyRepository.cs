using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class ExternalApiKeyRepository : GenericRepository<ExternalApiKey, Guid>, IExternalApiKeyRepository
{
    private readonly ExploreDbContext _dbContext;

    public ExternalApiKeyRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ExternalApiKey>> GetByOwner(ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalApiKeys
            .AsNoTracking()
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

            var chunkResults = await _dbContext.ExternalApiKeys
                .AsNoTracking()
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
        return await ManagementKeys().AsNoTracking()
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

    private IQueryable<ExternalApiKey> ManagementKeys()
    {
        Guid? tenantId = _dbContext.TenantFilterTenantId;
        return _dbContext.ExternalApiKeys
            .Where(key => !_dbContext.IsTenantFilterBypassed && tenantId != null && key.TenantId == tenantId);
    }
}
