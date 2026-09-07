namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IConfigurationImportOperationRepository
{
    Task AddAsync(
        ConfigurationImportOperation operation,
        CancellationToken cancellationToken);

    Task<ConfigurationImportOperation?> GetByIdAsync(
        Guid operationId,
        string targetAuthorityKey,
        CancellationToken cancellationToken);

    Task<ConfigurationImportOperation?> GetByIdForEffectAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationImportOperation>> ListAsync(
        string targetAuthorityKey,
        int maximumCount,
        CancellationToken cancellationToken);
}
