using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>All operations require the caller's transaction; no provider I/O is permitted.</summary>
public interface IEventResourceStorageLifecycleRepository
{
    Task<int> RetireExpiredUploadsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);

    Task RetireAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Physically removes detached, closed sources only while matching independent deletion authority exists.</summary>
    Task RemoveTransferredSourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken);

    Task RecordProducerSettlementAsync(EventResourceProducerIdentity identity, string? providerVersion,
        DateTime utcNow, CancellationToken cancellationToken);

    Task<StorageObject?> FenceActivationAsync(StorageUploadSession session, CancellationToken cancellationToken);
}

/// <summary>Captured before provider I/O; callbacks cannot select another source or target.</summary>
public sealed record EventResourceProducerIdentity(Guid TenantId, Guid SessionId, Guid ObjectId,
    string Provider, Guid BindingId, string ObjectKey);
