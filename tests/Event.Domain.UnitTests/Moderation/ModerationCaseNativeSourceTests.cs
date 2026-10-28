using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseNativeSourceTests
{
    [Test]
    public async Task HiddenMaterialUsesStableOccurrenceTargetWithoutExposingNativeResource()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var eventId = Guid.CreateVersion7();
        var eventVersion = Guid.CreateVersion7();
        var resource = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var occurrence = Guid.CreateVersion7();
        var occurrenceVersion = Guid.CreateVersion7();
        ModerationCaseTargetSource Source(char material) => ModerationCaseTargetSource.Record(
            Guid.CreateVersion7(), Guid.CreateVersion7(), tenant.Id, tenant.TrustDomainId,
            eventId, eventVersion, ModerationCaseTargetKind.HiddenContentReceipt, ModerationCaseTargetKind.Resource,
            resource, version, new string('a', 64), occurrence, occurrenceVersion, Guid.CreateVersion7(),
            new string('b', 64), new string(material, 64), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));
        var first = Source('c').Evidence(tenant);
        var second = Source('d').Evidence(tenant);
        await Assert.That(first.Target).IsEqualTo(second.Target);
        await Assert.That(first.Target.Id).IsEqualTo(occurrence);
        await Assert.That(first.Target.Id).IsNotEqualTo(resource);
        await Assert.That(first.SourceId).IsNotEqualTo(second.SourceId);
        await Assert.That(first.MaterialFactId).IsNotEqualTo(second.MaterialFactId);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task StandaloneSourceRetainsInstanceTargetWithoutInventingEventAncestry(ModerationCaseTargetKind kind)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var targetId = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var source = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            tenant.Id, tenant.TrustDomainId, null, null, kind, kind, targetId, version,
            new string('a', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(source.EventId).IsNull();
        await Assert.That(source.EventVersion).IsNull();
        await Assert.That(source.Evidence(tenant).Target.Origin).IsEqualTo(instance);
        await Assert.That(source.Evidence(tenant).Target.Id).IsEqualTo(targetId);
        await Assert.That(source.Evidence(tenant).Target.Version).IsEqualTo(version);
        await Assert.That(source.Evidence(tenant).SourceOrigin).IsEqualTo(tenant);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task EventOwnedSourcesCannotOmitTheirActualEvent(ModerationCaseTargetKind kind)
    {
        var nativeKind = kind is ModerationCaseTargetKind.HiddenContentReceipt or ModerationCaseTargetKind.LocalIncident
            ? ModerationCaseTargetKind.EventVersion : kind;
        await Assert.That(() => ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, kind, nativeKind,
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("01999999-1111-7111-8111-111111111111", null)]
    [Arguments(null, "01999999-1111-7111-8111-111111111111")]
    [Arguments("00000000-0000-0000-0000-000000000000", "01999999-1111-7111-8111-111111111111")]
    [Arguments("01999999-1111-7111-8111-111111111111", "00000000-0000-0000-0000-000000000000")]
    public async Task StandaloneSourcesRejectPartialOrEmptyEventAncestry(string? eventId, string? eventVersion)
    {
        var id = eventId is null ? (Guid?)null : Guid.Parse(eventId);
        var version = eventVersion is null ? (Guid?)null : Guid.Parse(eventVersion);
        await Assert.That(() => ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), id, version,
            ModerationCaseTargetKind.Actor, ModerationCaseTargetKind.Actor, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnversionedGrantIdentityCannotPretendToBeStampOrRevision()
    {
        var id = Guid.Parse("01999999-1111-7111-8111-111111111111");
        var identity = ModerationCaseRelationshipGeneration.FromIdentity(id);
        await Assert.That(identity.Identity).IsEqualTo(id);
        await Assert.That(identity.ConcurrencyStamp).IsNull();
        await Assert.That(identity.Revision).IsNull();
        await Assert.That(identity).IsNotEqualTo(ModerationCaseRelationshipGeneration.FromStamp(id));
        await Assert.That(() => ModerationCaseRelationshipGeneration.FromIdentity(Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task AdministratorReceiptBindsNativeGrantFactsWithoutInventingGrantVersion(ModerationCaseTargetKind kind)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var source = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            tenant.Id, tenant.TrustDomainId, null, null, kind, kind, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), now);
        var grantId = Guid.CreateVersion7();
        var receipt = Issue(ModerationCaseRelationshipGeneration.FromIdentity(grantId), new string('d', 64));
        receipt.ValidatePersistedState();
        await Assert.That(receipt.RelationshipSourceId).IsEqualTo(grantId);
        await Assert.That(receipt.RelationshipFactsDigest).IsEqualTo(new string('d', 64));
        await Assert.That(receipt.RelationshipGeneration.Identity).IsEqualTo(grantId);
        await Assert.That(receipt.RelationshipGeneration.Revision).IsNull();
        await Assert.That(receipt.RelationshipGeneration.ConcurrencyStamp).IsNull();
        await Assert.That(() => Issue(ModerationCaseRelationshipGeneration.FromStamp(grantId), new string('d', 64)))
            .Throws<ArgumentException>();
        await Assert.That(() => Issue(ModerationCaseRelationshipGeneration.FromRevision(1), new string('d', 64)))
            .Throws<ArgumentException>();
        await Assert.That(() => Issue(ModerationCaseRelationshipGeneration.FromIdentity(grantId), null))
            .Throws<ArgumentException>();

        ModerationCaseTargetReceipt Issue(ModerationCaseRelationshipGeneration generation, string? facts) =>
            ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, grantId,
                ModerationCaseRelationshipKind.InstanceAdministrator, generation, Guid.CreateVersion7(), 1,
                source.Evidence(tenant), new byte[32], now, now.AddMinutes(1), facts);
    }

    [Test]
    public async Task RelationshipGenerationsPreserveTheirNativeTypeAndAllBits()
    {
        var first = Guid.Parse("01999999-1111-7111-8111-111111111111");
        var second = Guid.Parse("01999999-1111-7111-8111-111111111112");
        await Assert.That(ModerationCaseRelationshipGeneration.FromStamp(first))
            .IsNotEqualTo(ModerationCaseRelationshipGeneration.FromStamp(second));
        await Assert.That(ModerationCaseRelationshipGeneration.FromStamp(first).ConcurrencyStamp).IsEqualTo(first);
        await Assert.That(ModerationCaseRelationshipGeneration.FromRevision(long.MaxValue).Revision).IsEqualTo(long.MaxValue);
        await Assert.That(() => ModerationCaseRelationshipGeneration.FromRevision(0)).Throws<ArgumentException>();
        await Assert.That(() => ModerationCaseRelationshipGeneration.FromStamp(Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    public async Task LifetimePolicySeparatesSubmissionAndReadAndRejectsStaleChanges()
    {
        var policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), ModerationCaseTargetKind.Form,
            TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), Guid.CreateVersion7());
        await Assert.That(policy.SubmissionLifetime).IsEqualTo(TimeSpan.FromMinutes(2));
        await Assert.That(policy.ReadLifetime).IsEqualTo(TimeSpan.FromSeconds(30));
        policy.Change(1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15), false, Guid.CreateVersion7());
        await Assert.That(policy.Revision).IsEqualTo(2);
        await Assert.That(policy.Enabled).IsFalse();
        await Assert.That(() => policy.Change(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            true, Guid.CreateVersion7())).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReadGrantCannotReplaceFiniteExpiryWithAnUnboundedSentinel()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(() => ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), false, now, DateTimeOffset.MaxValue))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReadGrantRevocationCannotBeUndoneByStaleRevision()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), false, now, now.AddMinutes(1));
        await Assert.That(grant.IsCurrentAt(now)).IsTrue();
        await Assert.That(grant.IsCurrentAt(now.AddMinutes(1))).IsFalse();
        grant.Revoke(1, now);
        await Assert.That(grant.IsCurrentAt(now)).IsFalse();
        await Assert.That(() => grant.Revoke(1, now)).Throws<InvalidOperationException>();
    }
}
