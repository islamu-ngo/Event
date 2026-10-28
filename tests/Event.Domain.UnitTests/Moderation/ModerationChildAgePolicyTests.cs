using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationChildAgePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Adoption_RequiresNominatedIndependentReviewer_WithoutRenewingSource()
    {
        var source = Source();
        foreach (var human in new[] { source.RecordedByHumanId, source.CollectionOwnerHumanId, Guid.CreateVersion7() })
            await Assert.That(() => ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
                human, 5, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 4, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        var adoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil)).IsFalse();
        await Assert.That(source.ValidUntil).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    public async Task Adoption_CurrentnessRequiresUtcWithinOriginalHalfOpenWindow()
    {
        var source = Source();
        var adoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now.AddTicks(-1))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, Now.ToOffset(TimeSpan.FromHours(1)))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil.AddTicks(-1))).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil)).IsFalse();
    }

    [Test]
    public async Task Withdrawal_AfterExpiryPreservesOriginalAdoption_AndIsTerminal()
    {
        var source = Source();
        var adoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        var withdrawer = Guid.CreateVersion7();
        adoption.Withdraw(1, withdrawer, Now.AddDays(1));
        await Assert.That(adoption.Revision).IsEqualTo(2);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(source.ReviewerHumanId);
        await Assert.That(adoption.SourceWriterGeneration).IsEqualTo(4);
        await Assert.That(adoption.WithdrawnByHumanId).IsEqualTo(withdrawer);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, withdrawer, Now.AddDays(2))).Throws<InvalidOperationException>();
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
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(18)]
    [Arguments(19)]
    public async Task Terms_RejectMissingSafeguardsOrUnreviewedGrounds(int change)
    {
        var terms = Terms();
        terms = change switch
        {
            0 => terms with { Collection = default },
            1 => terms with { Consent = default },
            2 => terms with { Assurance = default },
            3 => terms with { Evidence = default },
            4 => terms with { Classification = default },
            5 => terms with { DebateMinimumAge = 17 },
            6 => terms with { Audience = default },
            7 => terms with { Participant = default },
            8 => terms with { Correction = default },
            9 => terms with { ProtectedAccess = default },
            10 => terms with { ProhibitedInference = default },
            11 => terms with { Calendar = default },
            12 => terms with { TimeZone = default },
            13 => terms with { LeapDay = default },
            14 => terms with { Adulthood = default },
            15 => terms with { AdulthoodAge = 0 },
            16 => terms with { ChildRiskGrounds = "" },
            17 => terms with { PrivacyLegalGrounds = "" },
            18 => terms with { LawfulCollectionPurpose = "" },
            _ => terms with { ConsentGuardianApplicabilityGrounds = "" }
        };
        await Assert.That(terms.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task OptionalProviderAssurance_RequiresReview_AndNeverReplacesManualQualification()
    {
        var terms = Terms();
        await Assert.That(() => (terms with { ProviderAssurance = ModerationChildAgeProviderAssurance.OptionalReviewedThresholdOnly })
            .Validate()).Throws<ArgumentException>();
        await Assert.That(() => (terms with { ProviderAssuranceReviewGrounds = "Unselected provider grounds." })
            .Validate()).Throws<ArgumentException>();
        var reviewed = terms with
        {
            ProviderAssurance = ModerationChildAgeProviderAssurance.OptionalReviewedThresholdOnly,
            ProviderAssuranceReviewGrounds = "Reviewed minimization, threshold purpose, controller competence and revocation obligations."
        };
        reviewed.Validate();
        await Assert.That(() => (reviewed with { ManualAssuranceQualificationGrounds = "" }).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Ownership_RejectsSharedControllersAndMissingDualMandates()
    {
        var ownership = Ownership();
        await Assert.That(() => Source(ownership with { CollectionControllerHumanId = ownership.ReviewerControllerHumanId }))
            .Throws<ArgumentException>();
        await Assert.That(() => Source(ownership with { ReviewerHumanId = ownership.GovernanceControllerHumanId }))
            .Throws<ArgumentException>();
        await Assert.That(() => Source(ownership with { PrivacyLegalMandateBasis = "" })).Throws<ArgumentException>();
        await Assert.That(() => Source(ownership with { ChildSafeguardingMandateBasis = "" })).Throws<ArgumentException>();
        await Assert.That(() => Source(ownership with { IdentityAndIndependenceBasis = "" })).Throws<ArgumentException>();
    }

    [Test]
    public async Task Governance_RetainsReviewedCalendarAndPolicyFloor_NotEligibilityOrRestoration()
    {
        var source = Source();
        await Assert.That(source.Terms.DebateMinimumAge).IsEqualTo(18);
        await Assert.That(source.Terms.LeapDay).IsEqualTo(ModerationChildAgeLeapDay.MarchFirstInNonLeapYear);
        await Assert.That(source.Terms.Adulthood).IsEqualTo(ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes);
        await Assert.That(source.Terms.ProtectedAccess).IsEqualTo(ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent);
        await Assert.That(source.Terms.Correction).IsEqualTo(ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration);
        await Assert.That(source.Terms.Participant).IsEqualTo(ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser);
        await Assert.That(() => (source.Terms with { ClassificationRationale = "" }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (source.Terms with { CalendarJurisdictionGrounds = "" }).Validate()).Throws<ArgumentException>();
    }

    [Test]
    public async Task Source_retains_exact_ascii_hex_state_digest_without_deployment_model_authority()
    {
        foreach (var digest in new[] { "", new string('a', 63), new string('g', 64) })
        {
            var malformed = Source();
            typeof(ModerationChildAgePolicySource).GetProperty(nameof(malformed.ProposedStateDigest))!
                .SetValue(malformed, digest);
            await Assert.That(malformed.ValidatePersistedState).Throws<ArgumentException>();
        }
        var source = Source();
        var validDigest = "aB" + new string('0', 62);
        typeof(ModerationChildAgePolicySource).GetProperty(nameof(source.ProposedStateDigest))!
            .SetValue(source, validDigest);
        source.ValidatePersistedState();
        await Assert.That(source.ProposedStateDigest).IsEqualTo(validDigest);
    }

    private static ModerationChildAgePolicyOwnership Ownership()
    {
        var reviewer = Guid.CreateVersion7();
        var collection = Guid.CreateVersion7();
        return new(reviewer, reviewer, collection, collection, Guid.CreateVersion7(),
            "Reviewed distinct canonical controllers, competence and conflicts.",
            "Appointed independent reviewer of lawful purpose and privacy obligations.",
            "Appointed independent reviewer of child-risk and safeguarding obligations.",
            "Accountable native collection and correction owner.");
    }

    private static ModerationChildAgePolicySource Source(ModerationChildAgePolicyOwnership? ownership = null)
    {
        ownership ??= Ownership();
        return ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, new string('a', 64), Terms(), ownership,
            ownership.GovernanceControllerHumanId, 4, Now, Now.AddMinutes(5));
    }

    private static ModerationChildAgePolicyTerms Terms() => new(
        ModerationScopeKind.Tenant,
        ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
        ModerationChildAgeProviderAssurance.NotAccepted,
        ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat,
        18, ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
        ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
        ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
        "Reviewed child exposure and inappropriate audience risks.",
        "Reviewed jurisdiction, lawful basis, minimization and correction rights.",
        "Minimum private DOB or threshold only for actual subject eligibility.",
        "Review actual subject consent and guardian prerequisites where applicable before collection.",
        "Locally qualified manual threshold review with minimized provenance and finite evidence.",
        null, "Debate 18 is adopted ISLAMU policy, not universal legal or religious certification.",
        "Jurisdiction review adopts Gregorian UTC and conservative March 1 leap-day treatment.");
}
