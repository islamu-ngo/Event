using System.Security.Cryptography;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationCaseTargetReceiptTests
{
    [Test]
    public async Task NativeReceiptCannotUseAnUnboundedExpirySentinel()
    {
        var fixture = new ReceiptFixture();
        await Assert.That(() => ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), fixture.Tenant,
            fixture.CallerId, fixture.HostId, fixture.EpochId, 1, fixture.RelationshipId,
            ModerationCaseRelationshipKind.EventRole, ModerationCaseRelationshipGeneration.FromRevision(1),
            Guid.CreateVersion7(), 1, fixture.Evidence, fixture.Digest, fixture.Now, DateTimeOffset.MaxValue))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExactConsumptionRetryPreservesOneCaseAndOriginalDeadline()
    {
        var fixture = new ReceiptFixture();
        fixture.Consume();
        var consumedAt = fixture.Receipt.ConsumedAt;
        fixture.Consume(now: fixture.Now.AddMinutes(1), revision: 2);

        await Assert.That(fixture.Receipt.ConsumedCaseId).IsEqualTo(fixture.CaseId);
        await Assert.That(fixture.Receipt.ConsumedOperationId).IsEqualTo(fixture.OperationId);
        await Assert.That(fixture.Receipt.ConsumedAt).IsEqualTo(consumedAt);
        await Assert.That(fixture.Receipt.Revision).IsEqualTo(2);
        await Assert.That(fixture.Receipt.ExpiresAt).IsEqualTo(fixture.Now.AddMinutes(5));
        fixture.Receipt.ValidatePersistedState();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("caller")]
    [Arguments("host")]
    [Arguments("epoch")]
    [Arguments("qualification")]
    [Arguments("relationship")]
    [Arguments("relationship-generation")]
    [Arguments("target-version")]
    [Arguments("source-version")]
    [Arguments("occurrence")]
    [Arguments("material-fact")]
    [Arguments("token")]
    [Arguments("revision")]
    public async Task ChangedNativeQualificationCannotConsumeCopiedReceipt(string defect)
    {
        var fixture = new ReceiptFixture();
        var evidence = fixture.Evidence;
        var digest = (byte[])fixture.Digest.Clone();
        if (defect == "token")
            digest[0] ^= 1;
        if (defect is "target-version" or "source-version" or "occurrence" or "material-fact")
        {
            var target = defect == "target-version"
                ? ModerationCaseTarget.Create(evidence.Target.Kind, evidence.Target.Id,
                    evidence.Target.Origin, Guid.CreateVersion7()) : evidence.Target;
            evidence = ModerationEvidenceReference.Create(target, evidence.SourceOrigin,
                defect == "occurrence" ? Guid.CreateVersion7() : evidence.OccurrenceId,
                evidence.SourceId, defect == "source-version" ? Guid.CreateVersion7() : evidence.SourceVersion,
                defect == "material-fact" ? Guid.CreateVersion7() : evidence.MaterialFactId);
        }

        await Assert.That(() => fixture.Receipt.Consume(
            defect == "tenant" ? Guid.CreateVersion7() : fixture.Tenant.Id,
            defect == "caller" ? Guid.CreateVersion7() : fixture.CallerId,
            defect == "host" ? Guid.CreateVersion7() : fixture.HostId,
            defect == "epoch" ? Guid.CreateVersion7() : fixture.EpochId,
            defect == "qualification" ? 2 : 1,
            defect == "relationship" ? Guid.CreateVersion7() : fixture.RelationshipId,
            ModerationCaseRelationshipGeneration.FromRevision(defect == "relationship-generation" ? 2 : 1), evidence,
            digest,
            fixture.CaseId, fixture.OperationId, defect == "revision" ? 2 : 1, fixture.Now))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Receipt.Revision).IsEqualTo(1);
        await Assert.That(fixture.Receipt.ConsumedCaseId).IsNull();
        await Assert.That(fixture.Receipt.ConsumedAt).IsNull();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(300)]
    [Arguments(301)]
    public async Task ReceiptIsNotValidBeforeIssuanceOrAtAndAfterExpiry(int seconds)
    {
        var fixture = new ReceiptFixture();
        await Assert.That(() => fixture.Consume(now: fixture.Now.AddSeconds(seconds)))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Receipt.Revision).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ConsumedReceiptCannotAllocateAnotherCaseOrRequest(bool replaceCase)
    {
        var fixture = new ReceiptFixture();
        fixture.Consume();
        await Assert.That(() => fixture.Consume(
            caseId: replaceCase ? Guid.CreateVersion7() : fixture.CaseId,
            operationId: replaceCase ? fixture.OperationId : Guid.CreateVersion7(), revision: 2))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Receipt.ConsumedCaseId).IsEqualTo(fixture.CaseId);
        await Assert.That(fixture.Receipt.ConsumedOperationId).IsEqualTo(fixture.OperationId);
        await Assert.That(fixture.Receipt.Revision).IsEqualTo(2);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task IntakeReceiptNeverNeedsInventedResponsibleHuman(ModerationCaseTargetKind kind)
    {
        var fixture = new ReceiptFixture(kind);
        fixture.Consume();
        await Assert.That(fixture.Receipt.Evidence).IsEqualTo(fixture.Evidence);
        await Assert.That(fixture.Receipt.TargetKind).IsEqualTo(kind);
        await Assert.That(fixture.Receipt.CallerUserId).IsEqualTo(fixture.CallerId);
    }

    [Test]
    [Arguments(ModerationCaseRelationshipKind.EventRole)]
    [Arguments(ModerationCaseRelationshipKind.CaseReadGrant)]
    public async Task CounterOwnedRelationshipCannotAcceptAnUnrelatedGuidStamp(ModerationCaseRelationshipKind kind)
    {
        var fixture = new ReceiptFixture();
        await Assert.That(() => ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), fixture.Tenant,
            fixture.CallerId, fixture.HostId, fixture.EpochId, 1, fixture.RelationshipId,
            kind, ModerationCaseRelationshipGeneration.FromStamp(Guid.CreateVersion7()), Guid.CreateVersion7(), 1,
            fixture.Evidence, fixture.Digest, fixture.Now, fixture.Now.AddMinutes(1))).Throws<ArgumentException>();
    }

    [Test]
    public async Task GuidOwnedRelationshipCannotAcceptAnInventedRevisionOne()
    {
        var fixture = new ReceiptFixture();
        await Assert.That(() => ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), fixture.Tenant,
            fixture.CallerId, fixture.HostId, fixture.EpochId, 1, fixture.RelationshipId,
            ModerationCaseRelationshipKind.RegistrationParticipant, ModerationCaseRelationshipGeneration.FromRevision(1),
            Guid.CreateVersion7(), 1, fixture.Evidence, fixture.Digest, fixture.Now, fixture.Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReceiptCannotBeIssuedForAnotherTenantsMaterial()
    {
        var fixture = new ReceiptFixture();
        var foreignTenant = AuthorityScope.Tenant(fixture.Tenant.Parent!, Guid.CreateVersion7());
        await Assert.That(() => ModerationCaseTargetReceipt.Issue(
            Guid.CreateVersion7(), foreignTenant, fixture.CallerId, fixture.HostId, fixture.EpochId,
            1, fixture.RelationshipId, ModerationCaseRelationshipKind.EventRole,
            ModerationCaseRelationshipGeneration.FromRevision(1), Guid.CreateVersion7(), 1, fixture.Evidence, fixture.Digest,
            fixture.Now, fixture.Now.AddMinutes(5))).Throws<ArgumentException>();
    }

    private sealed class ReceiptFixture
    {
        public AuthorityScope Tenant { get; } = AuthorityScope.Tenant(
            AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7());
        public Guid CallerId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid EpochId { get; } = Guid.CreateVersion7();
        public Guid RelationshipId { get; } = Guid.CreateVersion7();
        public Guid CaseId { get; } = Guid.CreateVersion7();
        public Guid OperationId { get; } = Guid.CreateVersion7();
        public byte[] Digest { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public ModerationEvidenceReference Evidence { get; }
        public ModerationCaseTargetReceipt Receipt { get; }

        public ReceiptFixture(ModerationCaseTargetKind kind = ModerationCaseTargetKind.EventVersion)
        {
            Evidence = ModerationEvidenceReference.Create(
                ModerationCaseTarget.Create(kind, Guid.CreateVersion7(), Tenant, Guid.CreateVersion7()),
                Tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            Receipt = ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), Tenant, CallerId,
                HostId, EpochId, 1, RelationshipId, ModerationCaseRelationshipKind.EventRole,
                ModerationCaseRelationshipGeneration.FromRevision(1), Guid.CreateVersion7(), 1,
                Evidence, Digest, Now, Now.AddMinutes(5));
        }

        public void Consume(DateTimeOffset? now = null, Guid? caseId = null, Guid? operationId = null, long revision = 1) =>
            Receipt.Consume(Tenant.Id, CallerId, HostId, EpochId, 1, RelationshipId,
                ModerationCaseRelationshipGeneration.FromRevision(1), Evidence,
                Digest, caseId ?? CaseId, operationId ?? OperationId, revision, now ?? Now);
    }
}
