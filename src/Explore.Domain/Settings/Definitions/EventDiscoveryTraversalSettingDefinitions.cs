using System.Globalization;
using Explore.Domain.Constants;
using Explore.Domain.Services.Discovery;

namespace Explore.Domain.Settings.Definitions;

public static class EventDiscoveryTraversalSettingDefinitions
{
    public static readonly SettingDefinition MaxIdentities = Integer(
        GovernanceSettingKeys.EventDiscovery.MaxIdentities, EventDiscoveryTraversalLimits.IdentityCeiling,
        "Maximum canonical identities retained by one public discovery traversal.",
        [10, 25, 50, 100, 250, 500, 1000]);

    public static readonly SettingDefinition LifetimeMinutes = Integer(
        GovernanceSettingKeys.EventDiscovery.LifetimeMinutes, EventDiscoveryTraversalLimits.LifetimeMinutesCeiling,
        "Maximum membership lifetime in minutes, shortened by current eligibility boundaries.",
        Enumerable.Range(1, EventDiscoveryTraversalLimits.LifetimeMinutesCeiling));

    public static readonly SettingDefinition MaxLiveSnapshots = Integer(
        GovernanceSettingKeys.EventDiscovery.MaxLiveSnapshots, EventDiscoveryTraversalLimits.LiveSnapshotCeiling,
        "Maximum logically live discovery snapshots per tenant.",
        Enumerable.Range(1, EventDiscoveryTraversalLimits.LiveSnapshotCeiling));

    public static readonly SettingDefinition MaxPhysicalItems = Integer(
        GovernanceSettingKeys.EventDiscovery.MaxPhysicalItems, EventDiscoveryTraversalLimits.PhysicalItemCeiling,
        "Maximum physical membership rows per tenant, including expired rows; snapshot headers are bounded independently by the same ceiling.",
        [100, 1000, 10000, 50000, 100000, 200000, 400000]);

    public static readonly SettingDefinition MaxExaminedRows = Integer(
        GovernanceSettingKeys.EventDiscovery.MaxExaminedRows, EventDiscoveryTraversalLimits.ExaminedRowCeiling,
        "Maximum source rows examined while creating one discovery traversal.",
        [100, 500, 1000, 2500, 5000, 10000]);

    public static readonly SettingDefinition MaxSourceSeeks = Integer(
        GovernanceSettingKeys.EventDiscovery.MaxSourceSeeks, EventDiscoveryTraversalLimits.SourceSeekCeiling,
        "Maximum combined local and remote source seeks for one traversal.",
        Enumerable.Range(1, EventDiscoveryTraversalLimits.SourceSeekCeiling));

    public static IReadOnlyList<SettingDefinition> All =>
        [MaxIdentities, LifetimeMinutes, MaxLiveSnapshots, MaxPhysicalItems, MaxExaminedRows, MaxSourceSeeks];

    private static SettingDefinition Integer(string key, int defaultValue, string description, IEnumerable<int> values) =>
        new(key, SettingValueType.Integer, defaultValue.ToString(CultureInfo.InvariantCulture),
            "EventDiscovery", description, MaxScope: SettingScope.Tenant,
            AllowedValues: values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
}
