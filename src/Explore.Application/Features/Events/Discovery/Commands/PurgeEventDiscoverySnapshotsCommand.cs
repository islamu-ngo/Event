using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Events.Discovery.Commands;

public sealed record PurgeEventDiscoverySnapshotsCommand(Guid? AfterTenantId = null)
    : ICommand<PurgeEventDiscoverySnapshotsResult>;

public sealed record PurgeEventDiscoverySnapshotsResult(int DeletedSnapshots, Guid? NextTenantId);
