using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationCaseNarrativeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task OriginalCustodyHasItsOwnDeadlineRatherThanTheIntakeReceiptLifetime()
    {
        var fixture = new Fixture();
        var row = fixture.Capture();
        await Assert.That(row.ReceiptId).IsEqualTo(fixture.Receipt.Id);
        await Assert.That(row.SourceId).IsEqualTo(fixture.Receipt.SourceId);
        await Assert.That(row.CapturedAt).IsEqualTo(Now);
        await Assert.That(fixture.Receipt.ExpiresAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(row.PurgeAt).IsEqualTo(Now.AddMinutes(10));
    }

    [Test]
    public async Task ReadPurposeCannotCreateAnOriginalAllegationPayload()
    {
        var fixture = new Fixture();
        _ = fixture.Capture();
        var read = fixture.CreateReceipt(ModerationCaseRelationshipKind.CaseReadGrant);
        await Assert.That(() => fixture.Capture(receipt: read)).Throws<ArgumentException>();
    }

    [Test]
    public async Task CaptureCannotExceedTheOriginalRetentionTerms()
    {
        var fixture = new Fixture();
        await Assert.That(() => fixture.Capture(Now.AddMinutes(30).AddTicks(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CaptureCannotOutliveIndependentCustodyAdoption()
    {
        var fixture = new Fixture(TimeSpan.FromHours(2));
        await Assert.That(() => fixture.Capture(Now.AddHours(1).AddTicks(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task WithdrawnCustodyCannotCreateAnotherPayload()
    {
        var fixture = new Fixture();
        _ = fixture.Capture();
        fixture.Adoption.Withdraw(1, fixture.Policy.RecordedByHumanId, Now);
        await Assert.That(() => fixture.Capture()).Throws<ArgumentException>();
    }

    [Test]
    public async Task CaptureOwnsItsOpaquePayloadRatherThanTheCallersArray()
    {
        var fixture = new Fixture();
        var row = fixture.Capture();
        fixture.OpaquePayload[0] = 85;
        await Assert.That(row.ProtectedMaterial.Span[0]).IsEqualTo((byte)17);
    }

    [Test]
    public async Task ExactIntakeReceiptExpiryCannotCreatePayload()
    {
        var fixture = new Fixture();
        await Assert.That(() => fixture.Capture(capturedAt: Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    private sealed class Fixture
    {
        private readonly AuthorityScope tenant;
        private readonly Guid caseId = Guid.CreateVersion7();
        private readonly ModerationEvidenceReference evidence;
        private readonly Guid lifetimePolicyId = Guid.CreateVersion7();

        public Fixture(TimeSpan? maximumRetention = null)
        {
            tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            var recorder = Guid.CreateVersion7();
            var privacyOwner = Guid.CreateVersion7();
            Policy = ModerationCaseNarrativePolicySource.Record(
                Guid.CreateVersion7(), Guid.CreateVersion7(), tenant.TrustDomainId, tenant.Id,
                ModerationCaseTargetKind.Resource,
                new(ModerationCaseNarrativePurpose.ConfidentialAllegation,
                    maximumRetention ?? TimeSpan.FromMinutes(30), 4096),
                new(privacyOwner, Guid.CreateVersion7(), Guid.CreateVersion7(),
                    "Independent canonical controllers established by governing review.",
                    "Finite confidential allegation custody with independent lawful erasure."),
                recorder, 2, Now, Now.AddHours(2));
            Adoption = ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), Policy,
                privacyOwner, 3, Now, Now.AddHours(1));
            evidence = ModerationEvidenceReference.Create(
                ModerationCaseTarget.Create(ModerationCaseTargetKind.Resource,
                    Guid.CreateVersion7(), tenant, Guid.CreateVersion7()),
                tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7());
            Receipt = CreateReceipt(ModerationCaseRelationshipKind.EventRole);
            OpaquePayload = new byte[64];
            OpaquePayload[0] = 17;
        }

        public ModerationCaseNarrativePolicySource Policy { get; }
        public ModerationCaseNarrativeAdoption Adoption { get; }
        public ModerationCaseTargetReceipt Receipt { get; }
        public byte[] OpaquePayload { get; }

        public ModerationCaseTargetReceipt CreateReceipt(ModerationCaseRelationshipKind relationship) =>
            ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(),
                Policy.HostBindingId, Guid.CreateVersion7(), 3, Guid.CreateVersion7(), relationship,
                ModerationCaseRelationshipGeneration.FromRevision(1), lifetimePolicyId, 1,
                evidence, new byte[32], Now, Now.AddMinutes(1));

        public ModerationCaseNarrative Capture(
            DateTimeOffset? purgeAt = null, ModerationCaseTargetReceipt? receipt = null,
            DateTimeOffset? capturedAt = null) =>
            ModerationCaseNarrative.Capture(Guid.CreateVersion7(), caseId, receipt ?? Receipt,
                Policy, Adoption, capturedAt ?? Now, purgeAt ?? Now.AddMinutes(10), OpaquePayload);
    }
}
