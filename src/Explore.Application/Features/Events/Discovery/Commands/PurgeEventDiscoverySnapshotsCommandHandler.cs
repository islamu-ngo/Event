using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Events.Discovery.Commands;

public sealed class PurgeEventDiscoverySnapshotsCommandHandler(
    IEventDiscoverySnapshotMaintenanceRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<PurgeEventDiscoverySnapshotsCommand, PurgeEventDiscoverySnapshotsResult>
{
    public const int TenantBatchSize = 5;
    public const int SnapshotBatchSize = 10;

    public async Task<PurgeEventDiscoverySnapshotsResult> ExecuteAsync(
        PurgeEventDiscoverySnapshotsCommand request, CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        var owners = await unitOfWork.ExecuteReadCommittedAsync(
            token => repository.GetExpiredOwnersAsync(
                request.AfterTenantId, nowUtc, TenantBatchSize, token), cancellationToken);
        int deleted = 0;
        foreach (var owner in owners)
            deleted += await unitOfWork.ExecuteSerializableAsync(
                token => repository.PurgeTenantBatchAsync(owner.TenantId, nowUtc, SnapshotBatchSize, token),
                cancellationToken);
        return new(deleted, owners.Count == TenantBatchSize ? owners[^1].TenantId : null);
    }
}
