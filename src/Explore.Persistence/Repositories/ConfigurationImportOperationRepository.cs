namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

public sealed class ConfigurationImportOperationRepository(
    ExploreDbContext dbContext) : IConfigurationImportOperationRepository
{
    public async Task AddAsync(
        ConfigurationImportOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (ConfigurationRepositoryNativeOperations.ForInstanceImport(dbContext) is { } native)
        {
            await native.StoreOperationAsync(operation, cancellationToken);
            return;
        }
        await dbContext.ConfigurationImportOperations.AddAsync(
            operation,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConfigurationImportOperation?> GetByIdAsync(
        Guid operationId,
        string targetAuthorityKey,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAuthorityKey);
        if (ConfigurationRepositoryNativeOperations.ForInstanceImport(dbContext) is { } native)
            return await native.ReadOperationAsync(operationId, targetAuthorityKey, cancellationToken);
        return await dbContext.ConfigurationImportOperations
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
        if (ConfigurationRepositoryNativeOperations.ForInstanceImport(dbContext) is { } native)
            return await native.ListOperationsAsync(targetAuthorityKey, maximumCount, cancellationToken);
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
