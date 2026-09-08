using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IConfigurationManifestOperationRepository
{
    Task<ConfigurationManifestOperation> CreateAsync(
        ConfigurationManifestOperation operation,
        IReadOnlyCollection<ConfigurationManifestTenantResult> tenantResults,
        CancellationToken cancellationToken);

    Task<ConfigurationManifestOperation?> GetLatestByDigestAsync(
        string digest,
        CancellationToken cancellationToken);

    Task<ConfigurationManifestOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<ConfigurationManifestOperation?> GetLatestAppliedBootstrapAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationManifestTenantResult>> GetResultsByOperationIdAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<ConfigurationManifestTenantResult?> GetCurrentTenantResultAsync(
        Guid operationId,
        CancellationToken cancellationToken);
}

public interface IConfigurationManifestFailureRecorder
{
    Task RecordAsync(
        ConfigurationManifestOperation failedOperation,
        CancellationToken cancellationToken);
}
