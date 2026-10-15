using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationCaseNarrativePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(long.MaxValue)]
    public async Task RetentionRequiresAnExplicitFinitePositiveLimit(long ticks)
    {
        await Assert.That(() => new ModerationCaseNarrativeTerms(
            ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromTicks(ticks), 4096).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RecognitionOrUnknownPurposeCannotAuthorizeNarrativeCustody()
    {
        await Assert.That(() => new ModerationCaseNarrativeTerms(
            (ModerationCaseNarrativePurpose)99, TimeSpan.FromHours(1), 4096).Validate()).Throws<ArgumentException>();
    }

    [Test]
    public async Task InvestigatorCustodyRequiresItsOwnParticipantPurposeAndIndependentAdoption()
    {
        var source = ModerationCaseNarrativePolicySource.Record(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            ModerationCaseTargetKind.ParticipantIncident,
            new(ModerationCaseNarrativePurpose.ParticipantInvestigation, TimeSpan.FromMinutes(30), 4096),
            Owners(), Guid.CreateVersion7(), 2, Now, Now.AddHours(1));
        var adoption = ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 3, Now, Now.AddMinutes(30));
        await Assert.That(source.Purpose).IsEqualTo(ModerationCaseNarrativePurpose.ParticipantInvestigation);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(30))).IsFalse();
        await Assert.That(() => ModerationCaseNarrativePolicySource.Record(
            Guid.CreateVersion7(), source.HostBindingId, source.TrustDomainId, source.TenantId,
            ModerationCaseTargetKind.EventVersion, source.Terms, source.Ownership, source.RecordedByHumanId,
            4, Now, Now.AddHours(1))).Throws<ArgumentException>();
    }

    [Test]
    public async Task NominallyDifferentOwnersWithOneControllerCannotQualify()
    {
        var recorder = Guid.CreateVersion7();
        var controller = Guid.CreateVersion7();
        var ownership = new ModerationCaseNarrativeOwnership(Guid.CreateVersion7(), controller, controller,
            "Independent accountable controllers.", "Finite confidential allegation custody mandate.");
        await Assert.That(() => ownership.Validate(recorder)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EmptyMandateCannotBecomeAnAdministrativeCheckbox()
    {
        var ownership = Owners() with { PrivacyLegalMandateBasis = " " };
        await Assert.That(() => ownership.Validate(Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task AdoptionRequiresTheIndependentNamedPrivacyOwner()
    {
        var source = Source();
        await Assert.That(() => ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), source,
            source.RecordedByHumanId, 3, Now, Now.AddHours(1))).Throws<ArgumentException>();
    }

    [Test]
    public async Task WithdrawalAndExactClockCutoffPermanentlyRemoveCustodyAuthority()
    {
        var source = Source();
        var adoption = ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 3, Now, Now.AddHours(1));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddHours(1))).IsFalse();
        adoption.Withdraw(1, source.RecordedByHumanId, Now.AddMinutes(1));
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(2))).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, source.RecordedByHumanId, Now.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnotherPolicyGenerationCannotBorrowOriginalAdoption()
    {
        var source = Source();
        var adoption = ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 3, Now, Now.AddHours(1));
        var replacement = ModerationCaseNarrativePolicySource.Record(
            source.Id, source.HostBindingId, source.TrustDomainId, source.TenantId, source.TargetKind,
            source.Terms, source.Ownership, source.RecordedByHumanId, 4, source.RecordedAt, source.ValidUntil);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(replacement, Now)).IsFalse();
    }

    private static ModerationCaseNarrativeOwnership Owners() => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        "Independent canonical controllers established by governing review.",
        "Allegation-only finite custody; ordinary lawful erasure remains independent.");

    private static ModerationCaseNarrativePolicySource Source() => ModerationCaseNarrativePolicySource.Record(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        ModerationCaseTargetKind.Service,
        new(ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromHours(1), 4096),
        Owners(), Guid.CreateVersion7(), 2, Now, Now.AddHours(2));
}
