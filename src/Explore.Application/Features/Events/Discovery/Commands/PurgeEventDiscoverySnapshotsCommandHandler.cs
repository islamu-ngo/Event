using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Events.Discovery.Commands;

public sealed class PurgeEventDiscoverySnapshotsCommandHandler(
    IEventDiscoverySnapshotMaintenanceRepository repository,
    TimeProvider clock)
    : ICommandHandler<PurgeEventDiscoverySnapshotsCommand, PurgeEventDiscoverySnapshotsResult>
{
    public const int TenantBatchSize = 5;
    public const int SnapshotBatchSize = 10;

    public async Task<PurgeEventDiscoverySnapshotsResult> ExecuteAsync(
        PurgeEventDiscoverySnapshotsCommand request, CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        var owners = await repository.GetExpiredOwnersAsync(
            request.AfterTenantId, nowUtc, TenantBatchSize, cancellationToken);
        int deleted = 0;
        foreach (var owner in owners)
            deleted += await repository.PurgeTenantBatchAsync(
                owner.TenantId, nowUtc, SnapshotBatchSize, cancellationToken);
        return new(deleted, owners.Count == TenantBatchSize ? owners[^1].TenantId : null);
    }
}
