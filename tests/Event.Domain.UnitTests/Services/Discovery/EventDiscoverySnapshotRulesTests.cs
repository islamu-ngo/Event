using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Settings;

namespace Event.Domain.UnitTests.Services.Discovery;

public sealed class EventDiscoverySnapshotRulesTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("event_discovery.max_identities", "1000")]
    [Arguments("event_discovery.lifetime_minutes", "15")]
    [Arguments("event_discovery.max_live_snapshots", "200")]
    [Arguments("event_discovery.max_physical_items", "400000")]
    [Arguments("event_discovery.max_examined_rows", "10000")]
    [Arguments("event_discovery.max_source_seeks", "32")]
    public async Task Traversal_defaults_are_registered_machine_contracts(string key, string expected)
    {
        var definition = SettingRegistry.Get(key);
        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.DefaultValue).IsEqualTo(expected);
        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.Integer);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(definition.AllowedValues).IsNotNull();
        await Assert.That(definition.AllowedValues!.Contains(expected)).IsTrue();
    }

    [Test]
    public async Task Domain_defaults_preserve_the_approved_production_budgets()
    {
        var limits = new EventDiscoveryTraversalLimits();
        await Assert.That(limits.MaxIdentities).IsEqualTo(1000);
        await Assert.That(limits.Lifetime).IsEqualTo(TimeSpan.FromMinutes(15));
        await Assert.That(limits.MaxLiveSnapshots).IsEqualTo(200);
        await Assert.That(limits.MaxPhysicalItems).IsEqualTo(400000);
        await Assert.That(limits.MaxExaminedRows).IsEqualTo(10000);
        await Assert.That(limits.MaxSourceSeeks).IsEqualTo(32);
    }

    [Test]
    public async Task Snapshot_identity_is_reserved_before_retried_capture()
    {
        var id = Guid.CreateVersion7();
        var tenant = Guid.CreateVersion7();
        var firstAttempt = EventDiscoverySnapshot.Create(id, tenant, new string('a', 64),
            0, 0, Now, Now.AddMinutes(15), false, true, true, [], new());
        var replay = EventDiscoverySnapshot.Create(id, tenant, new string('a', 64),
            0, 0, Now, Now.AddMinutes(15), false, true, true, [], new());
        await Assert.That(firstAttempt.Id).IsEqualTo(id);
        await Assert.That(replay.Id).IsEqualTo(id);
        Assert.Throws<ArgumentException>(() => EventDiscoverySnapshot.Create(Guid.Empty, tenant,
            new string('a', 64), 0, 0, Now, Now.AddMinutes(15), false, true, true, [], new()));
    }

    [Test]
    public async Task Snapshot_timestamps_use_portable_precision_without_extending_expiry()
    {
        var created = Now.AddTicks(7);
        var expiry = created.AddMinutes(15);
        var snapshot = EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), 0, 0, created, expiry, false, true, true, [], new());
        await Assert.That(snapshot.CreatedAtUtc).IsEqualTo(Now);
        await Assert.That(snapshot.ExpiresAtUtc).IsEqualTo(Now.AddMinutes(15));
        await Assert.That(snapshot.ExpiresAtUtc <= expiry).IsTrue();
    }

    [Test]
    public async Task Capacity_counts_actual_expired_rows_and_cannot_overflow()
    {
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 2, maxLiveSnapshots: 2, maxPhysicalItems: 4);
        await Assert.That(limits.CanCapture(1, 2, 2)).IsTrue();
        await Assert.That(limits.CanCapture(2, 0, 1)).IsFalse();
        await Assert.That(limits.CanCapture(0, 4, 1)).IsFalse();
        await Assert.That(limits.CanCapture(0, long.MaxValue, 1)).IsFalse();
        await Assert.That(limits.CanCapture(0, 0, 3)).IsFalse();
    }

    [Test]
    public void Invalid_limits_cannot_disable_or_expand_hard_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxIdentities: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxIdentities: 1001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(lifetimeMinutes: 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxLiveSnapshots: 201));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxPhysicalItems: 400001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxExaminedRows: 10001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDiscoveryTraversalLimits(maxSourceSeeks: 33));
    }

    [Test]
    public async Task Membership_freezes_order_and_copies_caller_owned_members()
    {
        var member = EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, Guid.CreateVersion7(),
            EventDiscoveryCanonicalKind.LocalEvent, Guid.CreateVersion7(), Guid.CreateVersion7());
        var members = new List<EventDiscoverySnapshotItem> { member };
        var snapshot = EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
            7, 11, Now, Now.AddMinutes(3), false, true, true, members, new());
        members.Clear();
        member.TenantId = Guid.CreateVersion7();
        await Assert.That(snapshot.Items.Count).IsEqualTo(1);
        await Assert.That(snapshot.Items[0].Ordinal).IsEqualTo(0L);
        await Assert.That(snapshot.Items[0].TenantId).IsEqualTo(snapshot.TenantId);
        await Assert.That(snapshot.Items[0].SnapshotId).IsEqualTo(snapshot.Id);
        await Assert.That(snapshot.ExpiresAtUtc).IsEqualTo(Now.AddMinutes(3));
    }

    [Test]
    public void Duplicate_canonical_membership_is_rejected_but_namespaces_remain_distinct()
    {
        var id = Guid.CreateVersion7();
        var first = EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.LocalEvent, id,
            EventDiscoveryCanonicalKind.LocalEvent, id, null);
        var duplicate = EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.AtprotoRecord, Guid.CreateVersion7(),
            EventDiscoveryCanonicalKind.LocalEvent, id, null);
        Assert.Throws<ArgumentException>(() => EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), 0, 0, Now, Now.AddMinutes(15), false, true, true, [first, duplicate], new()));
        EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), 0, 0,
            Now, Now.AddMinutes(15), false, true, true,
            [first, EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.AtprotoRecord, id,
                EventDiscoveryCanonicalKind.AtprotoRecord, id, null)], new());
    }

    [Test]
    public void Expired_overlong_or_non_utc_membership_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), 0, 0, Now, Now, false, true, true, [], new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), 0, 0, Now, Now.AddMinutes(16), false, true, true, [], new()));
        Assert.Throws<ArgumentException>(() => EventDiscoverySnapshot.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), 0, 0, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified),
            Now.AddMinutes(15), false, true, true, [], new()));
    }
}
