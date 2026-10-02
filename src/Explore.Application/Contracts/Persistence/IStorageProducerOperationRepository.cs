using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Caller-owned transactions fence transfer from a producer to metadata or cleanup.</summary>
public interface IStorageProducerOperationRepository
{
    Task AddProducerAsync(StorageProducerOperation producer, CancellationToken cancellationToken);
    Task<StorageProducerOperation?> FenceProducerAsync(Guid id, Guid tenantId, CancellationToken cancellationToken);
    Task SettleProducerAsync(StorageProducerOperation identity, string? providerVersion, CancellationToken cancellationToken);
    Task CompleteProducerAsync(StorageProducerOperation producer, StorageObject storageObject, CancellationToken cancellationToken);
    Task RetireProducerAsync(Guid id, Guid tenantId, DateTime utcNow, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListKnownObjectKeysAsync(
        IReadOnlyCollection<Guid> bindingIds, IReadOnlyCollection<string> objectKeys, CancellationToken cancellationToken);
}
