using Explore.Domain.Services.Discovery;

namespace Event.Domain.UnitTests.Services.Discovery;

public sealed class EventDiscoveryIdentityRulesTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    [Test]
    public async Task Cross_tenant_decision_fails_without_partial_graph_or_epoch()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(Guid.CreateVersion7());
        var revision = Revision(a.TenantId);
        await Assert.That(() => Review(revision, [a, b], a, b)).Throws<InvalidOperationException>();
        await Assert.That(a.Alias).IsNull();
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);
    }

    [Test]
    public async Task Stale_expected_revision_does_not_reassign_any_member()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var revision = Revision(a.TenantId);
        revision.IdentityEpoch = 9;
        await Assert.That(() => Review(revision, [a, b], a, b)).Throws<InvalidOperationException>();
        await Assert.That(a.Alias).IsNull();
        await Assert.That(revision.IdentityEpoch).IsEqualTo(9);
    }

    [Test]
    public async Task Self_alias_is_rejected_without_advancing_epoch()
    {
        var a = Identity(Guid.CreateVersion7());
        var revision = Revision(a.TenantId);
        await Assert.That(() => Review(revision, [a], a, a)).Throws<InvalidOperationException>();
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);
    }

    [Test]
    public async Task Selecting_an_existing_member_reassigns_every_relationship_directly()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var c = Identity(a.TenantId);
        var revision = Revision(a.TenantId);
        Review(revision, [a, b, c], b, a);
        Review(revision, [a, b, c], c, a, 1);
        Review(revision, [a, b, c], a, b, 2);
        await Assert.That(b.Alias).IsNull();
        await Assert.That(a.Alias?.PrimaryIdentityId).IsEqualTo(b.Id);
        await Assert.That(c.Alias?.PrimaryIdentityId).IsEqualTo(b.Id);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(3);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(0);
    }

    [Test]
    public async Task Private_primary_never_supplies_a_public_members_representation()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        Review(Revision(a.TenantId), [a, b], b, a);
        await Assert.That(EventDiscoveryIdentityRules.SelectRepresentation([a, b], new HashSet<Guid> { b.Id }))
            .IsSameReferenceAs(b);
        await Assert.That(EventDiscoveryIdentityRules.SelectPublicPrimary([a, b], new HashSet<Guid> { b.Id }))
            .IsNull();
        await Assert.That(EventDiscoveryIdentityRules.SelectPublicPrimary([a, b], new HashSet<Guid> { a.Id }))
            .IsSameReferenceAs(a);
    }

    [Test]
    public async Task Eligible_member_does_not_need_its_unavailable_primary_loaded()
    {
        var primary = Identity(Guid.CreateVersion7());
        var member = Identity(primary.TenantId);
        Review(Revision(primary.TenantId), [primary, member], member, primary);
        await Assert.That(EventDiscoveryIdentityRules.SelectRepresentation([member], new HashSet<Guid> { member.Id }))
            .IsSameReferenceAs(member);
        await Assert.That(EventDiscoveryIdentityRules.SelectPublicPrimary([member], new HashSet<Guid> { member.Id }))
            .IsNull();
    }

    [Test]
    public async Task Reverse_detaches_only_the_reviewed_member_and_preserves_source_identifiers()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var c = Identity(a.TenantId);
        var originalId = b.Id;
        var originalKey = b.SourceKey;
        var revision = Revision(a.TenantId);
        Review(revision, [a, b, c], b, a);
        Review(revision, [a, b, c], c, a, 1);
        EventDiscoveryIdentityRules.Reverse(revision, 2, [a, b, c], b.Id, a.Id,
            Reviewer, "different_offering", Now);
        await Assert.That(b.Alias).IsNull();
        await Assert.That(c.Alias?.PrimaryIdentityId).IsEqualTo(a.Id);
        await Assert.That(b.Id).IsEqualTo(originalId);
        await Assert.That(b.SourceKey).IsEqualTo(originalKey);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(3);
    }

    [Test]
    public async Task Stale_reversal_keeps_all_relationships_intact()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var revision = Revision(a.TenantId);
        Review(revision, [a, b], b, a);
        await Assert.That(() => EventDiscoveryIdentityRules.Reverse(revision, 0, [a, b], b.Id, a.Id,
            Reviewer, "different_offering", Now)).Throws<InvalidOperationException>();
        await Assert.That(b.Alias?.PrimaryIdentityId).IsEqualTo(a.Id);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Existing_chain_or_cycle_is_rejected_before_reassignment(bool cycle)
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var c = Identity(a.TenantId);
        a.Alias = Alias(a, b);
        b.Alias = Alias(b, cycle ? a : c);
        var revision = Revision(a.TenantId);
        revision.IdentityEpoch = 1;
        await Assert.That(() => Review(revision, [a, b, c], a, c, 1)).Throws<InvalidOperationException>();
        await Assert.That(a.Alias.PrimaryIdentityId).IsEqualTo(b.Id);
        await Assert.That(b.Alias.PrimaryIdentityId).IsEqualTo(cycle ? a.Id : c.Id);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(1);
    }

    [Test]
    public async Task Source_namespace_collision_is_not_an_identity_collision()
    {
        var tenantId = Guid.CreateVersion7();
        var key = Guid.CreateVersion7().ToString("D");
        var local = Identity(tenantId, key);
        var remote = EventDiscoveryIdentity.Create(tenantId, EventDiscoverySourceKind.AtprotoRecord, key);
        var revision = Revision(tenantId);
        Review(revision, [local, remote], remote, local);
        await Assert.That(remote.Id).IsNotEqualTo(local.Id);
        await Assert.That(remote.Alias?.PrimaryIdentityId).IsEqualTo(local.Id);
    }

    [Test]
    public async Task Tombstone_never_reappears_or_falls_back_to_a_suppressed_remote_echo()
    {
        var local = Identity(Guid.CreateVersion7());
        var echo = EventDiscoveryIdentity.Create(local.TenantId, EventDiscoverySourceKind.AtprotoRecord, "at://did:plc:example/event/one");
        Review(Revision(local.TenantId), [local, echo], echo, local);
        local.IsDeleted = true;
        // Existing source authority excludes the bound remote echo. Even an obsolete local eligibility
        // entry cannot resurrect its tombstoned source binding.
        await Assert.That(EventDiscoveryIdentityRules.SelectRepresentation([local, echo],
            new HashSet<Guid> { local.Id })).IsNull();
        await Assert.That(EventDiscoveryIdentityRules.SelectPublicPrimary([local, echo],
            new HashSet<Guid> { local.Id })).IsNull();
    }

    [Test]
    public async Task Revision_overflow_cannot_leave_a_partially_reassigned_graph()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var revision = Revision(a.TenantId);
        revision.IdentityEpoch = long.MaxValue;
        await Assert.That(() => Review(revision, [a, b], a, b, long.MaxValue)).Throws<OverflowException>();
        await Assert.That(a.Alias).IsNull();
        await Assert.That(b.Alias).IsNull();
        await Assert.That(revision.IdentityEpoch).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task Two_groups_merge_under_the_selected_member_without_chains()
    {
        var a = Identity(Guid.CreateVersion7());
        var b = Identity(a.TenantId);
        var c = Identity(a.TenantId);
        var d = Identity(a.TenantId);
        var revision = Revision(a.TenantId);
        Review(revision, [a, b, c, d], b, a);
        Review(revision, [a, b, c, d], d, c, 1);
        Review(revision, [a, b, c, d], a, d, 2);
        await Assert.That(d.Alias).IsNull();
        await Assert.That(new[] { a, b, c }.All(identity => identity.Alias?.PrimaryIdentityId == d.Id)).IsTrue();
        await Assert.That(revision.IdentityEpoch).IsEqualTo(3);
    }

    private static EventDiscoveryAlias Alias(EventDiscoveryIdentity member, EventDiscoveryIdentity primary) =>
        new()
        {
            Id = Guid.CreateVersion7(), TenantId = member.TenantId,
            MemberIdentityId = member.Id, PrimaryIdentityId = primary.Id,
            Member = member, Primary = primary, RelationshipRevision = 1
        };

    internal static EventDiscoveryIdentity Identity(Guid tenantId, string? key = null) =>
        EventDiscoveryIdentity.Create(tenantId, EventDiscoverySourceKind.LocalEvent, key ?? Guid.CreateVersion7().ToString("D"));

    private static EventDiscoveryRevision Revision(Guid tenantId) =>
        new() { Id = Guid.CreateVersion7(), TenantId = tenantId };

    private static void Review(
        EventDiscoveryRevision revision, IReadOnlyCollection<EventDiscoveryIdentity> graph,
        EventDiscoveryIdentity member, EventDiscoveryIdentity primary, long expected = 0) =>
        EventDiscoveryIdentityRules.Review(revision, expected, graph, member.Id, primary.Id,
            Reviewer, "same_offering", Now);
}
