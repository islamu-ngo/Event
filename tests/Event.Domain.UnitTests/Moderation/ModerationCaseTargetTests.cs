using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationCaseTargetTests
{
    [Test]
    public async Task MatchingIdentifiersDoNotCollapseContentPublisherAndServiceTargets()
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var content = ModerationCaseTarget.Create(ModerationCaseTargetKind.EventVersion, id, tenant, version);
        var publisher = ModerationCaseTarget.Create(ModerationCaseTargetKind.Actor, id, tenant.Parent!, version);
        var service = ModerationCaseTarget.Create(ModerationCaseTargetKind.Service, id, tenant.Parent!, version);

        await Assert.That(content == publisher).IsFalse();
        await Assert.That(publisher == service).IsFalse();
        await Assert.That(content == service).IsFalse();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task IncidentTargetRetainsLocalIdentityWithoutConstructingAHuman(
        ModerationCaseTargetKind kind)
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var target = ModerationCaseTarget.Create(kind, id, tenant, version);

        await Assert.That(target.Kind).IsEqualTo(kind);
        await Assert.That(target.Id).IsEqualTo(id);
        await Assert.That(target.Origin).IsEqualTo(tenant);
        await Assert.That(target.Version).IsEqualTo(version);
        await Assert.That(() => ModerationCaseTarget.Create(kind, id, tenant.Parent!, version))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task TenantTargetsCannotLoseTheirOwningTenant(ModerationCaseTargetKind kind)
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();

        await Assert.That(() => ModerationCaseTarget.Create(kind, id, tenant.Parent!, version))
            .Throws<ArgumentException>();
        await Assert.That(() => ModerationCaseTarget.Create(
            kind, id, AuthorityScope.Resource(tenant, id), version)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task InstanceOwnedTargetsCannotAcquireAnInventedTenantOrigin(ModerationCaseTargetKind kind)
    {
        await Assert.That(() => ModerationCaseTarget.Create(
            kind, Guid.CreateVersion7(), Tenant(), Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task TargetIdentityRequiresKnownKindIdentifierAndVersion(int defect)
    {
        await Assert.That(() => ModerationCaseTarget.Create(
            defect == 0 ? (ModerationCaseTargetKind)0 : ModerationCaseTargetKind.EventVersion,
            defect == 1 ? Guid.Empty : Guid.CreateVersion7(), Tenant(),
            defect == 2 ? Guid.Empty : Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task SameLocalIdentifierAtAnotherOriginOrVersionIsDifferentMaterial()
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var target = ModerationCaseTarget.Create(ModerationCaseTargetKind.EventVersion, id, tenant, version);
        var otherTenant = AuthorityScope.Tenant(tenant.Parent!, Guid.CreateVersion7());
        var otherInstance = AuthorityScope.Instance(tenant.Parent!.Parent!, Guid.CreateVersion7());
        var copiedTenantId = AuthorityScope.Tenant(otherInstance, tenant.Id);

        await Assert.That(target == ModerationCaseTarget.Create(
            target.Kind, id, otherTenant, version)).IsFalse();
        await Assert.That(target == ModerationCaseTarget.Create(
            target.Kind, id, copiedTenantId, version)).IsFalse();
        await Assert.That(target == ModerationCaseTarget.Create(
            target.Kind, id, tenant, Guid.CreateVersion7())).IsFalse();
    }

    [Test]
    public async Task DestinationAndResourceRemainDistinctEvenWhenOneRowOwnsBoth()
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var resource = ModerationCaseTarget.Create(ModerationCaseTargetKind.Resource, id, tenant, version);
        var destination = ModerationCaseTarget.Create(ModerationCaseTargetKind.Destination, id, tenant, version);

        await Assert.That(resource == destination).IsFalse();
    }

    [Test]
    public async Task ExactProvenanceRetryIsEqualButNewOccurrenceSourceVersionOrMaterialFactIsNot()
    {
        var target = ModerationCaseTarget.Create(
            ModerationCaseTargetKind.ParticipantIncident, Guid.CreateVersion7(), Tenant(), Guid.CreateVersion7());
        var occurrence = Guid.CreateVersion7();
        var source = Guid.CreateVersion7();
        var sourceVersion = Guid.CreateVersion7();
        var material = Guid.CreateVersion7();
        var reference = ModerationEvidenceReference.Create(target, target.Origin, occurrence, source, sourceVersion, material);
        var exactRetry = ModerationEvidenceReference.Create(target, target.Origin, occurrence, source, sourceVersion, material);
        var different = new[]
        {
            ModerationEvidenceReference.Create(target, target.Origin, Guid.CreateVersion7(), source, sourceVersion, material),
            ModerationEvidenceReference.Create(target, target.Origin, occurrence, Guid.CreateVersion7(), sourceVersion, material),
            ModerationEvidenceReference.Create(target, target.Origin, occurrence, source, Guid.CreateVersion7(), material),
            ModerationEvidenceReference.Create(target, target.Origin, occurrence, source, sourceVersion, Guid.CreateVersion7()),
            ModerationEvidenceReference.Create(target, target.Origin.Parent!, occurrence, source, sourceVersion, material),
            ModerationEvidenceReference.Create(ModerationCaseTarget.Create(target.Kind, target.Id,
                target.Origin, Guid.CreateVersion7()), target.Origin, occurrence, source, sourceVersion, material)
        };

        await Assert.That(reference == exactRetry).IsTrue();
        await Assert.That(new HashSet<ModerationEvidenceReference> { reference, exactRetry }.Count).IsEqualTo(1);
        foreach (var next in different)
            await Assert.That(reference == next).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EvidenceProvenanceCannotOmitAnyIdentityCoordinate(int defect)
    {
        var target = ModerationCaseTarget.Create(
            ModerationCaseTargetKind.Resource, Guid.CreateVersion7(), Tenant(), Guid.CreateVersion7());
        await Assert.That(() => ModerationEvidenceReference.Create(target, target.Origin,
            defect == 0 ? Guid.Empty : Guid.CreateVersion7(),
            defect == 1 ? Guid.Empty : Guid.CreateVersion7(),
            defect == 2 ? Guid.Empty : Guid.CreateVersion7(),
            defect == 3 ? Guid.Empty : Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task DiagnosticFormattingDoesNotDiscloseTargetOrEvidenceCoordinates()
    {
        var target = ModerationCaseTarget.Create(
            ModerationCaseTargetKind.HiddenContentReceipt, Guid.CreateVersion7(), Tenant(), Guid.CreateVersion7());
        var evidence = ModerationEvidenceReference.Create(
            target, target.Origin, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        foreach (var id in new[] { target.Id, target.Version, target.Origin.Id, target.Origin.TrustDomainId,
                     evidence.OccurrenceId, evidence.SourceId, evidence.SourceVersion, evidence.MaterialFactId })
        {
            await Assert.That(target.ToString().Contains(id.ToString("D"), StringComparison.Ordinal)).IsFalse();
            await Assert.That(evidence.ToString().Contains(id.ToString("D"), StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task EvidenceCannotBorrowSourceProvenanceFromAnotherTenantOrInstance()
    {
        var tenant = Tenant();
        var target = ModerationCaseTarget.Create(
            ModerationCaseTargetKind.EventVersion, Guid.CreateVersion7(), tenant, Guid.CreateVersion7());
        var otherTenant = AuthorityScope.Tenant(tenant.Parent!, Guid.CreateVersion7());
        var otherInstance = AuthorityScope.Instance(tenant.Parent!.Parent!, Guid.CreateVersion7());
        foreach (var origin in new[] { otherTenant, otherInstance, Tenant() })
        {
            await Assert.That(() => ModerationEvidenceReference.Create(target, origin,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()))
                .Throws<ArgumentException>();
        }
    }

    private static AuthorityScope Tenant() => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
}
