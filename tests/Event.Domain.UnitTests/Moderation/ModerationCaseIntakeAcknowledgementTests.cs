using System.Security.Cryptography;
using System.Text;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseIntakeAcknowledgementTests
{
    [Test]
    public async Task AcknowledgementRetainsOriginalAcceptanceAfterNewMaterialAndClosure()
    {
        var fixture = new IntakeFixture();
        fixture.Consume();
        var acknowledgement = fixture.Capture();
        var next = ModerationEvidenceReference.Create(fixture.Evidence.Target, fixture.Scope,
            fixture.Evidence.OccurrenceId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        fixture.Case.RecordEvidence(next, 1, fixture.Now.AddMinutes(1));
        fixture.Case.Close(2, fixture.Now.AddMinutes(2));

        acknowledgement.ValidatePersistedState();
        await Assert.That(acknowledgement.CaseRevision).IsEqualTo(1);
        await Assert.That(acknowledgement.EvidenceCaseRevision).IsEqualTo(1);
        await Assert.That(acknowledgement.ReceiptRevision).IsEqualTo(2);
        await Assert.That(acknowledgement.AcceptedAt).IsEqualTo(fixture.Now);
        await Assert.That(acknowledgement.BudgetSourceId).IsEqualTo(fixture.Budget.Id);
        await Assert.That(acknowledgement.BudgetAdoptionId).IsEqualTo(fixture.Adoption.Id);
        await Assert.That(acknowledgement.MatchesRequest(fixture.CallerId,
            fixture.OperationId, fixture.Receipt.Id, fixture.Digest)).IsTrue();
    }

    [Test]
    [Arguments("caller")]
    [Arguments("operation")]
    [Arguments("receipt")]
    [Arguments("digest")]
    public async Task ExactRequestCannotBeReplacedByAnotherCallerOrMaterial(string defect)
    {
        var fixture = new IntakeFixture();
        fixture.Consume();
        var acknowledgement = fixture.Capture();
        var digest = (byte[])fixture.Digest.Clone();
        if (defect == "digest") digest[0] ^= 1;
        await Assert.That(acknowledgement.MatchesRequest(
            defect == "caller" ? Guid.CreateVersion7() : fixture.CallerId,
            defect == "operation" ? Guid.CreateVersion7() : fixture.OperationId,
            defect == "receipt" ? Guid.CreateVersion7() : fixture.Receipt.Id, digest)).IsFalse();
    }

    [Test]
    [Arguments("unconsumed")]
    [Arguments("another-case")]
    [Arguments("attributed")]
    public async Task StorageAcknowledgementCannotSubstituteForActualOwnedIntake(string defect)
    {
        var fixture = new IntakeFixture();
        var allegation = fixture.Case;
        if (defect != "unconsumed")
            fixture.Consume(caseId: defect == "another-case" ? Guid.CreateVersion7() : fixture.Case.Id);
        if (defect == "attributed")
            allegation = ModerationCase.Open(fixture.Case.Id,
                SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), fixture.Scope.Parent!),
                fixture.Scope, fixture.Evidence.OccurrenceId, fixture.Now);

        await Assert.That(() => ModerationCaseIntakeAcknowledgement.Capture(
            Guid.CreateVersion7(), allegation, fixture.Receipt, fixture.Digest, fixture.Budget, fixture.Adoption))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("foreign-source")]
    [Arguments("expired")]
    [Arguments("withdrawn")]
    public async Task AcceptanceCannotBorrowAnotherBudgetOrRenewAnExpiredSelection(string change)
    {
        var fixture = new IntakeFixture();
        fixture.Consume();
        var source = fixture.Budget;
        var adoption = fixture.Adoption;
        if (change == "foreign-source")
            source = ModerationOperationalBudgetSource.Record(Guid.CreateVersion7(), source.HostBindingId,
                source.TrustDomainId, source.TenantId, source.Terms, source.OperatorHumanId,
                source.RecordedByHumanId, 4, fixture.Now, fixture.Now.AddHours(1));
        else if (change == "expired")
            adoption = ModerationOperationalBudgetAdoption.Adopt(Guid.CreateVersion7(), source,
                source.OperatorHumanId, 3, fixture.Now.AddMinutes(-2), fixture.Now);
        else
            adoption.Withdraw(1, source.OperatorHumanId, fixture.Now);
        await Assert.That(() => ModerationCaseIntakeAcknowledgement.Capture(
            Guid.CreateVersion7(), fixture.Case, fixture.Receipt, fixture.Digest, source, adoption))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClosedOccurrenceStillAcceptsReporterWithoutChangingOriginalClosure()
    {
        var fixture = new IntakeFixture();
        fixture.Case.Close(1, fixture.Now);
        fixture.Consume();
        var acknowledgement = fixture.Capture();
        acknowledgement.ValidatePersistedState();
        await Assert.That(acknowledgement.MatchesRequest(
            fixture.CallerId, fixture.OperationId, fixture.Receipt.Id, fixture.Digest)).IsTrue();
        await Assert.That(fixture.Case.State).IsEqualTo(ModerationCaseState.Closed);
        await Assert.That(acknowledgement.CaseRevision).IsEqualTo(2L);
        await Assert.That(acknowledgement.EvidenceCaseRevision).IsEqualTo(1L);
    }

    [Test]
    public async Task DiagnosticFormattingDoesNotExposePrivateOwnershipOrRequestDigest()
    {
        var fixture = new IntakeFixture();
        fixture.Consume();
        var acknowledgement = fixture.Capture();
        var formatted = acknowledgement.ToString();
        foreach (var privateValue in new[]
                 {
                     fixture.CallerId.ToString(), fixture.Case.Id.ToString(),
                     fixture.OperationId.ToString(), acknowledgement.RequestDigest
                 })
            await Assert.That(formatted.Contains(privateValue, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("infinite")]
    [Arguments("non-UTC")]
    public async Task Persisted_acceptance_requires_a_finite_UTC_timestamp(string defect)
    {
        var fixture = new IntakeFixture();
        fixture.Consume();
        var acknowledgement = fixture.Capture();
        var invalid = defect == "infinite" ? DateTimeOffset.MaxValue
            : fixture.Now.ToOffset(TimeSpan.FromHours(1));
        typeof(ModerationCaseIntakeAcknowledgement)
            .GetProperty(nameof(ModerationCaseIntakeAcknowledgement.AcceptedAt))!
            .SetValue(acknowledgement, invalid);

        await Assert.That(acknowledgement.ValidatePersistedState).Throws<ArgumentException>();
    }

    private sealed class IntakeFixture
    {
        public AuthorityScope Scope { get; } = AuthorityScope.Tenant(
            AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        public Guid CallerId { get; } = Guid.CreateVersion7();
        public Guid OperationId { get; } = Guid.CreateVersion7();
        public byte[] Token { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public ModerationEvidenceReference Evidence { get; }
        public ModerationCase Case { get; }
        public ModerationCaseTargetReceipt Receipt { get; }
        public byte[] Digest { get; }
        public ModerationOperationalBudgetSource Budget { get; }
        public ModerationOperationalBudgetAdoption Adoption { get; }

        public IntakeFixture()
        {
            Evidence = ModerationEvidenceReference.Create(
                ModerationCaseTarget.Create(ModerationCaseTargetKind.LocalIncident,
                    Guid.CreateVersion7(), Scope, Guid.CreateVersion7()),
                Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            Case = ModerationCase.OpenAllegation(Guid.CreateVersion7(), Scope, Evidence, Now);
            Receipt = ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), Scope, CallerId,
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
                ModerationCaseRelationshipKind.EventRole, ModerationCaseRelationshipGeneration.FromRevision(1),
                Guid.CreateVersion7(), 1,
                Evidence, SHA256.HashData(Token), Now, Now.AddMinutes(5));
            Digest = HMACSHA256.HashData(Token, Encoding.UTF8.GetBytes(OperationId.ToString("N")));
            Budget = ModerationOperationalBudgetSource.Record(Guid.CreateVersion7(), Receipt.HostBindingId,
                Scope.TrustDomainId, Scope.Id,
                new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1)),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 2, Now.AddMinutes(-5), Now.AddHours(2));
            Adoption = ModerationOperationalBudgetAdoption.Adopt(Guid.CreateVersion7(), Budget,
                Budget.OperatorHumanId, 3, Now.AddMinutes(-2), Now.AddHours(1));
        }

        public void Consume(Guid? caseId = null) => Receipt.Consume(
            Scope.Id, CallerId, Receipt.HostBindingId, Receipt.QualificationEpochId,
            Receipt.QualificationRevision, Receipt.RelationshipSourceId, Receipt.RelationshipGeneration,
            Evidence, SHA256.HashData(Token), caseId ?? Case.Id, OperationId, 1, Now);

        public ModerationCaseIntakeAcknowledgement Capture() =>
            ModerationCaseIntakeAcknowledgement.Capture(Guid.CreateVersion7(), Case, Receipt, Digest, Budget, Adoption);
    }
}
