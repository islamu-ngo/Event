using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Append-only target identities, deliberately independent of tenant query filters.</summary>
public interface IStorageProviderBindingRepository
{
    Task<StorageProviderBinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(StorageProviderBinding binding, CancellationToken cancellationToken);
}
