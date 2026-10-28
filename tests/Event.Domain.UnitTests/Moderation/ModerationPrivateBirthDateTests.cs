using System.Text.Json;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationPrivateBirthDateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BirthDate = new(2008, 10, 14);

    [Test]
    public async Task Optional_identity_has_no_birth_date_or_collection_reference()
    {
        var fixture = new Fixture();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
        await Assert.That(fixture.Pii.ReadBirthDateCollectionReceiptId()).IsNull();
        await Assert.That(fixture.Pii.ReadBirthDateRevision()).IsEqualTo(0L);
    }

    [Test]
    public async Task Current_human_identity_receipt_records_only_a_private_declaration()
    {
        var fixture = new Fixture();
        var ordinaryJson = JsonSerializer.Serialize(fixture.Pii);
        fixture.Record();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsEqualTo(BirthDate);
        await Assert.That(fixture.Pii.ReadBirthDateCollectionReceiptId()).IsEqualTo(fixture.Receipt.Id);
        await Assert.That(fixture.Pii.ReadBirthDateRevision()).IsEqualTo(1L);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(fixture.Pii));
        await Assert.That(json.RootElement.GetRawText()).IsEqualTo(ordinaryJson);
        await Assert.That(json.RootElement.TryGetProperty("DateOfBirth", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("BirthDateCollectionReceiptId", out _)).IsFalse();
    }

    [Test]
    public async Task Another_humans_receipt_cannot_capture_identity_birth_date()
    {
        var fixture = new Fixture();
        fixture.Pii.UserId = Guid.CreateVersion7();
        await Assert.That(() => fixture.Record()).Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
    }

    [Test]
    public async Task Participant_collection_cannot_be_repurposed_for_identity_pii()
    {
        var fixture = new Fixture(ModerationIndividualPurpose.ParticipantCollection);
        await Assert.That(() => fixture.Record()).Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
    }

    [Test]
    public async Task Receipt_expiry_is_exclusive_and_does_not_capture_birth_date()
    {
        var fixture = new Fixture();
        await Assert.That(() => fixture.Record(now: fixture.Receipt.ValidUntil)).Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
    }

    [Test]
    public async Task Withdrawn_receipt_does_not_capture_birth_date()
    {
        var fixture = new Fixture();
        fixture.Receipt.Retire(1, ModerationIndividualRecordState.Withdrawn, Now);
        await Assert.That(() => fixture.Record()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Changed_native_applicability_cannot_reuse_collection_receipt()
    {
        var fixture = new Fixture();
        var changedAssessment = new ModerationCollectionPrerequisiteAssessment(fixture.Binding,
            new ModerationIndividualNativeReference(Guid.CreateVersion7(), 1, 2, Now, Now.AddMinutes(5)),
            [ModerationCollectionPrerequisiteKind.SubjectConsent]);
        await Assert.That(() => fixture.Pii.RecordDeclaredBirthDate(
            BirthDate, 0, fixture.Receipt, fixture.Binding, changedAssessment, [], Now))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
    }

    [Test]
    public async Task Another_native_observation_cannot_reuse_collection_receipt()
    {
        var fixture = new Fixture();
        var changedBinding = new ModerationIndividualBinding(
            fixture.Binding.Target, new ModerationAgePolicy(
                fixture.Binding.PolicyId, fixture.Binding.PolicyRevision, fixture.Binding.Jurisdiction),
            fixture.Binding.Purpose, fixture.Binding.SourceId, 1, 1,
            Guid.CreateVersion7(), 2, Now, Now.AddMinutes(5));
        await Assert.That(() => fixture.Pii.RecordDeclaredBirthDate(
            BirthDate, 0, fixture.Receipt, changedBinding, fixture.Assessment, [], Now))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadBirthDateCollectionReceiptId()).IsNull();
    }

    [Test]
    public async Task Stale_correction_cannot_overwrite_newer_private_declaration()
    {
        var fixture = new Fixture(ModerationIndividualPurpose.ProfileCompletion);
        fixture.Record();
        await Assert.That(() => fixture.Record(new DateOnly(2007, 10, 14), 0))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsEqualTo(BirthDate);
        fixture.Record(new DateOnly(2007, 10, 14), 1);
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsEqualTo(new DateOnly(2007, 10, 14));
        await Assert.That(fixture.Pii.ReadBirthDateRevision()).IsEqualTo(2L);
    }

    [Test]
    public async Task Clearing_private_declaration_requires_no_live_collection_or_consent()
    {
        var fixture = new Fixture();
        fixture.Record();
        fixture.Receipt.Retire(1, ModerationIndividualRecordState.Withdrawn, Now);
        fixture.Pii.ClearDeclaredBirthDate(1);
        await Assert.That(fixture.Pii.ReadDeclaredBirthDate()).IsNull();
        await Assert.That(fixture.Pii.ReadBirthDateCollectionReceiptId()).IsNull();
        await Assert.That(fixture.Pii.ReadBirthDateRevision()).IsEqualTo(2L);
        await Assert.That(() => fixture.Pii.ClearDeclaredBirthDate(1)).Throws<InvalidOperationException>();
    }

    private sealed class Fixture
    {
        public UserPii Pii { get; }
        public ModerationIndividualBinding Binding { get; }
        public ModerationCollectionPrerequisiteAssessment Assessment { get; }
        public ModerationCollectionReceipt Receipt { get; }

        public Fixture(ModerationIndividualPurpose purpose = ModerationIndividualPurpose.IdentityEnrollment)
        {
            var instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7());
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
            Pii = new() { UserId = subject.Id, Email = "", FirstName = "", LastName = "" };
            Binding = new(ModerationIndividualTarget.Human(subject),
                new ModerationAgePolicy(Guid.CreateVersion7(), 1, instance), purpose,
                Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7(), 1, Now, Now.AddMinutes(5));
            Assessment = new(Binding,
                new ModerationIndividualNativeReference(Guid.CreateVersion7(), 1, 1, Now, Now.AddMinutes(5)), []);
            Receipt = ModerationCollectionReceipt.Record(
                Guid.CreateVersion7(), Binding, Assessment, [], Now, Now.AddMinutes(4));
        }

        public void Record(DateOnly? birthDate = null, long expectedRevision = 0, DateTimeOffset? now = null) =>
            Pii.RecordDeclaredBirthDate(birthDate ?? BirthDate, expectedRevision,
                Receipt, Binding, Assessment, [], now ?? Now);
    }
}
