using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class EventResourceStorageLifecycleService(
    IEventResourceStorageLifecycleRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>Caller has persisted detachment; admission preserves other physical references and holds.</summary>
    public Task<StorageRetirementAdmission> TryQueueRetirementAsync(Guid tenantId, Guid objectId,
        DateTime utcNow, CancellationToken cancellationToken) =>
        repository.TryQueueRetirementAsync(tenantId, objectId, utcNow, cancellationToken);

    /// <summary>Participates in the native caller's detachment/redaction transaction.</summary>
    public Task RetireAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, DateTime utcNow, CancellationToken cancellationToken) =>
        repository.RetireAsync(tenantId, resourceIds, objectIds, utcNow, cancellationToken);

    /// <summary>Caller owns the transaction and has already saved detachment and retirement accounting.</summary>
    public Task RemoveTransferredSourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken) =>
        repository.RemoveTransferredSourcesAsync(tenantId, resourceIds, objectIds, cancellationToken);

    public Task<StorageObject?> FenceActivationAsync(StorageUploadSession session, CancellationToken cancellationToken) =>
        repository.FenceActivationAsync(session, cancellationToken);

    /// <summary>Commits independently of attachment and required-success audit.</summary>
    public Task RecordProducerSettlementAsync(EventResourceProducerIdentity identity, string? providerVersion,
        CancellationToken cancellationToken) => unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            await repository.RecordProducerSettlementAsync(identity, providerVersion, clock.GetUtcNow().UtcDateTime, ct);
            return true;
        }, cancellationToken);
}
