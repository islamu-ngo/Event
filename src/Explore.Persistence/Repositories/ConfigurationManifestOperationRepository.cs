using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Explore.Persistence.Database.ProviderPrimitives;

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
        ConfigurationRepositoryNativeOperations.ForInstanceImport(dbContext) is { } native
            ? native.ReadLatestAppliedBootstrapAsync(cancellationToken)
            : dbContext.ConfigurationManifestOperations
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
        if (ConfigurationRepositoryNativeOperations.ForManifestReplay(dbContext) is { } native)
            return await native.ReadManifestResultsAsync(operationId, cancellationToken);
        if (!await dbContext.ConfigurationManifestOperations.AsNoTracking().AnyAsync(
                operation => operation.Id == operationId
                    && operation.Status == ConfigurationManifestOperationStatus.Applied
                    && operation.Mode == ConfigurationManifestAuditMode.Bootstrap, cancellationToken))
            return [];
        List<ConfigurationManifestTenantResult> results = await dbContext.ConfigurationManifestTenantResults
            .IgnoreTenantFilter(TenantFilterBypassReasons.ConfigurationManifestOperationReplay)
            .AsNoTracking()
            .Where(result => result.OperationId == operationId)
            .OrderBy(result => result.TenantId)
            .Take(ConfigurationManifestValidator.MaximumTenantCount + 1)
            .ToListAsync(cancellationToken);
        if (results.Count > ConfigurationManifestValidator.MaximumTenantCount)
            throw new InvalidOperationException("Manifest replay result bound was exceeded.");
        return results;
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
