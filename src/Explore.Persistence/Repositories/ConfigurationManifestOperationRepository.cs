using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ConfigurationManifestOperationRepository(ExploreDbContext dbContext)
    : IConfigurationManifestOperationRepository
{
    public async Task<ConfigurationManifestOperation> CreateAsync(
        ConfigurationManifestOperation operation,
        IReadOnlyCollection<ConfigurationManifestTenantResult> tenantResults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(tenantResults);
        if (operation.Status == ConfigurationManifestOperationStatus.Failed && tenantResults.Count != 0)
        {
            throw new ArgumentException("Failed manifest operations cannot retain tenant results.", nameof(tenantResults));
        }

        if (tenantResults.Any(result => result.OperationId != operation.Id))
        {
            throw new ArgumentException("Every tenant result must belong to the supplied operation.", nameof(tenantResults));
        }

        dbContext.ConfigurationManifestOperations.Add(operation);
        dbContext.ConfigurationManifestTenantResults.AddRange(tenantResults);
        await dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    public Task<ConfigurationManifestOperation?> GetLatestByDigestAsync(
        string digest,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        return dbContext.ConfigurationManifestOperations
            .AsNoTracking()
            .Where(operation => operation.Digest == digest)
            .OrderByDescending(operation => operation.CompletedAt)
            .ThenByDescending(operation => operation.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<ConfigurationManifestOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        return dbContext.ConfigurationManifestOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
    }

    public Task<ConfigurationManifestOperation?>
        GetLatestAppliedBootstrapAsync(
            CancellationToken cancellationToken) =>
        dbContext.ConfigurationManifestOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Status
                    == ConfigurationManifestOperationStatus.Applied
                && operation.InstanceSectionDigest != null
                && operation.BootstrapGeneration != null)
            .OrderByDescending(operation => operation.BootstrapGeneration)
            .ThenByDescending(operation => operation.CompletedAt)
            .ThenByDescending(operation => operation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConfigurationManifestTenantResult>>
        GetResultsByOperationIdAsync(
            Guid operationId,
            CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        return await dbContext.ConfigurationManifestTenantResults
            .IgnoreTenantFilter(TenantFilterBypassReasons.ConfigurationManifestOperationReplay)
            .AsNoTracking()
            .Where(result => result.OperationId == operationId)
            .OrderBy(result => result.TenantId)
            .ToListAsync(cancellationToken);
    }

    public Task<ConfigurationManifestTenantResult?> GetCurrentTenantResultAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        return dbContext.ConfigurationManifestTenantResults
            .AsNoTracking()
            .Include(result => result.Operation)
            .SingleOrDefaultAsync(result => result.OperationId == operationId, cancellationToken);
    }
}
