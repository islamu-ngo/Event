namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class ConfigurationImportOperationRepository(
    ExploreDbContext dbContext) : IConfigurationImportOperationRepository
{
    public async Task AddAsync(
        ConfigurationImportOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await dbContext.ConfigurationImportOperations.AddAsync(
            operation,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ConfigurationImportOperation?> GetByIdAsync(
        Guid operationId,
        string targetAuthorityKey,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAuthorityKey);
        return dbContext.ConfigurationImportOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.Id == operationId
                    && operation.TargetAuthorityKey == targetAuthorityKey,
                cancellationToken);
    }

    public Task<ConfigurationImportOperation?> GetByIdForEffectAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        return dbContext.ConfigurationImportOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.Id == operationId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ConfigurationImportOperation>> ListAsync(
        string targetAuthorityKey,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAuthorityKey);
        if (maximumCount is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        return await dbContext.ConfigurationImportOperations
            .AsNoTracking()
            .Where(operation =>
                operation.TargetAuthorityKey == targetAuthorityKey)
            .OrderByDescending(operation => operation.StartedAt)
            .ThenByDescending(operation => operation.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    }
}
