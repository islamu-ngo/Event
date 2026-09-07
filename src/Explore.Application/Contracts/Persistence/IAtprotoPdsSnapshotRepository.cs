using Explore.Application.Features.Federation.Atproto.Models;

namespace Explore.Application.Contracts.Persistence;

public sealed record AtprotoPdsSnapshotApplyRequest(
    AtprotoJetstreamClaim Claim,
    IReadOnlyList<string> ScannedDids,
    IReadOnlyList<AtprotoPdsSnapshot> Snapshots,
    IReadOnlyList<Guid> PresentationTenantIds,
    long SnapshotVersion,
    DateTime ObservedAt)
{
    public IReadOnlyList<AtprotoFederatedEventImportPlan> EventImports { get; init; } = [];
}

public interface IAtprotoPdsSnapshotRepository
{
    Task<bool> TryReconcileAsync(
        AtprotoPdsSnapshotApplyRequest request,
        CancellationToken cancellationToken);

    Task<AtprotoPersistenceApplyResult> TryReconcileWithResultAsync(
        AtprotoPdsSnapshotApplyRequest request,
        CancellationToken cancellationToken);
}
