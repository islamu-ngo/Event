using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CustomPropertyProjectionStatusRepository : ICustomPropertyProjectionStatusRepository
{
    private readonly ExploreDbContext _dbContext;

    public CustomPropertyProjectionStatusRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<CustomPropertyProjectionStatus?> GetAsync(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.CustomPropertyProjectionStatuses
            .FirstOrDefaultAsync(
                e => e.ProjectionName == projectionName
                    && e.ProjectionVersion == projectionVersion
                    && e.TenantId == tenantId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<CustomPropertyProjectionStatus>> GetAllForProjectionAsync(
        string projectionName,
        int projectionVersion,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CustomPropertyProjectionStatuses
            .AsNoTracking()
            .Where(e => e.ProjectionName == projectionName && e.ProjectionVersion == projectionVersion)
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomPropertyProjectionStatus> UpsertAsync(
        CustomPropertyProjectionStatus row,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CustomPropertyProjectionStatuses
            .FirstOrDefaultAsync(
                e => e.ProjectionName == row.ProjectionName
                    && e.ProjectionVersion == row.ProjectionVersion
                    && e.TenantId == row.TenantId,
                cancellationToken);

        if (existing is null)
        {
            _dbContext.CustomPropertyProjectionStatuses.Add(row);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return row;
        }

        existing.State = row.State;
        existing.LastRebuildStartedAt = row.LastRebuildStartedAt;
        existing.LastRebuildCompletedAt = row.LastRebuildCompletedAt;
        existing.RowsProcessed = row.RowsProcessed;
        existing.RowsFailed = row.RowsFailed;
        existing.LastCheckpoint = row.LastCheckpoint;
        existing.LastErrorMessage = row.LastErrorMessage;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task MarkStateAsync(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        CustomPropertyProjectionState state,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CustomPropertyProjectionStatuses
            .FirstOrDefaultAsync(
                e => e.ProjectionName == projectionName
                    && e.ProjectionVersion == projectionVersion
                    && e.TenantId == tenantId,
                cancellationToken);

        if (existing is null)
        {
            existing = new CustomPropertyProjectionStatus
            {
                ProjectionName = projectionName,
                ProjectionVersion = projectionVersion,
                TenantId = tenantId,
                State = state,
                LastErrorMessage = errorMessage,
            };
            _dbContext.CustomPropertyProjectionStatuses.Add(existing);
        }
        else
        {
            existing.State = state;
            existing.LastErrorMessage = errorMessage;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
