namespace Explore.Domain.Services.Discovery;

/// <summary>Finite tenant budgets. Operators may lower, but never disable, the platform ceilings.</summary>
public sealed record EventDiscoveryTraversalLimits
{
    public const int IdentityCeiling = 1000;
    public const int LifetimeMinutesCeiling = 15;
    public const int LiveSnapshotCeiling = 200;
    public const int PhysicalItemCeiling = 400000;
    public const int ExaminedRowCeiling = 10000;
    public const int SourceSeekCeiling = 32;
    public const int PageSizeCeiling = 100;
    public const int PurgeBatchCeiling = 100;

    public EventDiscoveryTraversalLimits(
        int maxIdentities = IdentityCeiling,
        int lifetimeMinutes = LifetimeMinutesCeiling,
        int maxLiveSnapshots = LiveSnapshotCeiling,
        int maxPhysicalItems = PhysicalItemCeiling,
        int maxExaminedRows = ExaminedRowCeiling,
        int maxSourceSeeks = SourceSeekCeiling)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIdentities, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxIdentities, IdentityCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(lifetimeMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetimeMinutes, LifetimeMinutesCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLiveSnapshots, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLiveSnapshots, LiveSnapshotCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPhysicalItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPhysicalItems, PhysicalItemCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExaminedRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxExaminedRows, ExaminedRowCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSourceSeeks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSourceSeeks, SourceSeekCeiling);
        MaxIdentities = maxIdentities;
        Lifetime = TimeSpan.FromMinutes(lifetimeMinutes);
        MaxLiveSnapshots = maxLiveSnapshots;
        MaxPhysicalItems = maxPhysicalItems;
        MaxExaminedRows = maxExaminedRows;
        MaxSourceSeeks = maxSourceSeeks;
    }

    public int MaxIdentities { get; }
    public TimeSpan Lifetime { get; }
    public int MaxLiveSnapshots { get; }
    public int MaxPhysicalItems { get; }
    public int MaxExaminedRows { get; }
    public int MaxSourceSeeks { get; }

    public bool CanCapture(long liveSnapshots, long physicalItems, int proposedItems) =>
        liveSnapshots >= 0 && liveSnapshots < MaxLiveSnapshots &&
        physicalItems >= 0 && physicalItems <= MaxPhysicalItems &&
        proposedItems >= 0 && proposedItems <= MaxIdentities &&
        proposedItems <= MaxPhysicalItems - physicalItems;
}
