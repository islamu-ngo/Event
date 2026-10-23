using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionPrivacyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Adoption_RequiresNominatedIndependentOwner_AndKeepsOriginalFiniteLease()
    {
        var source = Source();
        await Assert.That(() => ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.RecordedByHumanId, 5, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 5, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 4, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        var adoption = ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(4))).IsFalse();
        adoption.Withdraw(1, Guid.CreateVersion7(), Now.AddHours(1));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, Guid.CreateVersion7(), Now.AddHours(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task Policy_RejectsUnboundedOrMissingSubstantiveObligations(int change)
    {
        var terms = Terms();
        terms = change switch
        {
            0 => terms with { Purposes = 0 },
            1 => terms with { OrdinaryErasure = default },
            2 => terms with { CatastrophicDisposition = default },
            3 => terms with { EscalationAfter = terms.LawfulRequestDeadline },
            4 => terms with { LawfulRequestDeadline = TimeSpan.MaxValue },
            5 => terms with { BackupSafetyMargin = TimeSpan.Zero },
            6 => terms with { KeyObligations = default },
            _ => terms with { Correction = default }
        };
        await Assert.That(terms.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task Retention_RequiresEveryClass_AndNeverHoldsLiveMatchingBeyondEligibility()
    {
        var retention = Retention();
        await Assert.That(() => Source(retention: retention.Take(3))).Throws<ArgumentException>();
        await Assert.That(() => Source(retention: [.. retention, retention[0]])).Throws<ArgumentException>();
        retention[1] = retention[1] with
        {
            Hold = ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim,
            MaximumHold = TimeSpan.FromDays(1),
            HoldReviewWithin = TimeSpan.FromHours(1)
        };
        await Assert.That(() => Source(retention: retention)).Throws<ArgumentException>();
        retention = Retention();
        retention[2] = retention[2] with { MaximumHold = TimeSpan.MaxValue };
        await Assert.That(() => Source(retention: retention)).Throws<ArgumentException>();
        retention = Retention();
        retention[2] = retention[2] with { MaximumRetention = TimeSpan.FromDays(180) };
        await Assert.That(() => Source(retention: retention)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Policy_RejectsNominalIndependenceAndAbsentMandateGrounds()
    {
        var ownership = Ownership();
        await Assert.That(() => Source(ownership: ownership with
        { RecoveryControllerHumanId = ownership.PrivacyControllerHumanId })).Throws<ArgumentException>();
        await Assert.That(() => Source(ownership: ownership with
        { PrivacyLegalMandateBasis = "" })).Throws<ArgumentException>();
        await Assert.That(() => Source(ownership: ownership with
        { IdentityAndIndependenceBasis = "" })).Throws<ArgumentException>();
    }

    [Test]
    public async Task Policy_KeepsBackupReplayAndReadableKeyObligationsSeparateFromEnforcement()
    {
        var source = Source();
        await Assert.That(source.Terms.OrdinaryErasure)
            .IsEqualTo(ModerationRecognitionPrivacyOrdinaryErasure.IndependentExistingAuthority);
        await Assert.That(source.Terms.Linkage)
            .IsEqualTo(ModerationRecognitionPrivacyLinkage.ExactFinalActiveSanctionScopeRevisionAndInterval);
        await Assert.That(source.MinimumReadableKeyHorizon).IsEqualTo(TimeSpan.FromDays(402));
        await Assert.That(source.Terms.CatastrophicDisposition)
            .IsEqualTo(ModerationRecognitionPrivacyCatastrophicDisposition.QualifiedRecoveryOrLegalErasureByDeadline);
        await Assert.That(source.Retention[0].ToTerms().Erasure)
            .IsEqualTo(ModerationRecognitionPrivacyErasureRule.SettleOrQualifiedDispositionByLawfulDeadline);
        await Assert.That(source.Retention[1].ToTerms().Erasure)
            .IsEqualTo(ModerationRecognitionPrivacyErasureRule.EarlierSanctionExpiryReversalPurposeLossOrRetentionCeiling);
        await Assert.That(source.Retention[2].ToTerms().RetentionStart)
            .IsEqualTo(ModerationRecognitionPrivacyRetentionStart.LatestQualifiedRemedyNotification);
        await Assert.That(source.Retention[3].ToTerms().Erasure)
            .IsEqualTo(ModerationRecognitionPrivacyErasureRule.BackupReplayFloorThenFiniteJustifiedHold);
    }

    internal static ModerationRecognitionPrivacyTerms Terms() => new(
        ModerationRecognitionPrivacyPurpose.ScopedActionContinuity, ModerationScopeKind.Tenant,
        ModerationRecognitionPrivacyLinkage.ExactFinalActiveSanctionScopeRevisionAndInterval,
        ModerationRecognitionPrivacyOrdinaryErasure.IndependentExistingAuthority,
        ModerationRecognitionPrivacyCorrection.RevisionBoundRemoveOnlyAffectedContributionAndPreserveRemedies,
        ModerationRecognitionPrivacyCatastrophicDisposition.QualifiedRecoveryOrLegalErasureByDeadline,
        ModerationRecognitionPrivacyKeyObligations.OriginalComputedTokensAndAcknowledgedReplicaBackupHorizon,
        6, TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromDays(30), TimeSpan.FromDays(7));

    internal static ModerationRecognitionPrivacyRetentionTerms[] Retention() =>
    [
        new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
            ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
        new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
            ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
        new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
            ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim, TimeSpan.FromDays(30), TimeSpan.FromDays(7)),
        new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
            ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim, TimeSpan.FromDays(30), TimeSpan.FromDays(7))
    ];

    [Test]
    public async Task Adoption_CurrentnessRequiresUtcWithinTheOriginalHalfOpenWindow()
    {
        var source = Source();
        var adoption = ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.PrivacyOwnerHumanId, 5, Now, Now.AddMinutes(4));

        await Assert.That(adoption.MatchesCurrent(source, Now.AddTicks(-1))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, Now.ToOffset(TimeSpan.FromHours(2)))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil.AddTicks(-1))).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil)).IsFalse();
    }

    private static ModerationRecognitionPrivacyOwnership Ownership()
    {
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        return new(privacy, privacy, recovery, recovery, Guid.CreateVersion7(),
            "Canonical controllers independently reviewed; no delegated alias supplies independence.",
            "Appointed privacy owner reviews lawful purpose, request deadlines, retention and remedies.",
            "Appointed recovery owner assesses durable material and qualified legal disposition before deadline.");
    }

    private static ModerationRecognitionPrivacySource Source(
        ModerationRecognitionPrivacyOwnership? ownership = null,
        IEnumerable<ModerationRecognitionPrivacyRetentionTerms>? retention = null)
    {
        ownership ??= Ownership();
        return ModerationRecognitionPrivacySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64),
            Terms(), ownership, retention ?? Retention(), ownership.GovernanceControllerHumanId,
            4, Now, Now.AddMinutes(5));
    }
}
