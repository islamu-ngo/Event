using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class CustomPropertyProjectionDirtyScopeRepository : ICustomPropertyProjectionDirtyScopeRepository
{
    private readonly ExploreDbContext _dbContext;

    public CustomPropertyProjectionDirtyScopeRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task UpsertAsync(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        CustomPropertyProjectionScopeType scopeType,
        Guid scopeId,
        Guid? definitionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CustomPropertyProjectionDirtyScopes
            .FirstOrDefaultAsync(
                e => e.ProjectionName == projectionName
                    && e.ProjectionVersion == projectionVersion
                    && e.TenantId == tenantId
                    && e.ScopeType == scopeType
                    && e.ScopeId == scopeId
                    && e.DefinitionId == definitionId
                    && e.DrainedAt == null,
                cancellationToken);

        if (existing is not null)
        {
            existing.Reason = reason;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            return;
        }

        _dbContext.CustomPropertyProjectionDirtyScopes.Add(new CustomPropertyProjectionDirtyScope
        {
            ProjectionName = projectionName,
            ProjectionVersion = projectionVersion,
            TenantId = tenantId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            DefinitionId = definitionId,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public async Task<IReadOnlyList<CustomPropertyProjectionDirtyScope>> GetPendingAsync(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        int batchSize,
        CancellationToken cancellationToken,
        int skip = 0)
    {
        return await _dbContext.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .Where(e => e.ProjectionName == projectionName
                && e.ProjectionVersion == projectionVersion
                && e.TenantId == tenantId
                && e.DrainedAt == null)
            .OrderBy(e => e.Id)
            .Skip(skip)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkDrainedAsync(
        IReadOnlyCollection<long> ids,
        DateTimeOffset drainedAt,
        CancellationToken cancellationToken)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Projection draining requires the exact tenant Serializable transaction.");
        if (ids.Count == 0)
        {
            return;
        }

        await _dbContext.CustomPropertyProjectionDirtyScopes
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id) && e.DrainedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(e => e.DrainedAt, drainedAt),
                cancellationToken);
    }

    public Task<int> CountPendingAsync(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.CustomPropertyProjectionDirtyScopes
            .CountAsync(
                e => e.ProjectionName == projectionName
                    && e.ProjectionVersion == projectionVersion
                    && e.TenantId == tenantId
                    && e.DrainedAt == null,
                cancellationToken);
    }
}
