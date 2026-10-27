using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationClassificationTaxonomyTests
{
    [Test]
    [Arguments(0, "general", 0)]
    [Arguments(1, "General", 0)]
    [Arguments(1, "general ", 0)]
    [Arguments(1, "general", -1)]
    [Arguments(1, "general", 131)]
    public async Task Category_RejectsInvalidLookupCodeAndFloor(int lookup, string code, int floor)
    {
        var category = Category(lookup, code, floor);
        await Assert.That(() => category.Validate(18)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Debate_UsesAdoptedFloorRatherThanLabelOrFormat()
    {
        var debate = Category(1, "discussion", 17) with
        { Kind = EventContentClassificationVersion.CategoryKind.Debate };
        await Assert.That(() => debate.Validate(18)).Throws<ArgumentException>();
        (debate with { MinimumAge = 18 }).Validate(18);
        Category(2, "debate", 7).Validate(18);
        await Assert.That((debate with { MinimumAge = 18 }).MinimumAge).IsEqualTo(18);
    }

    [Test]
    public async Task Category_RejectsUnknownSemanticKindAndEmptyReviewedMeaning()
    {
        var category = Category(1, "general", 6);
        await Assert.That(() => (category with { Kind = (EventContentClassificationVersion.CategoryKind)99 }).Validate(18))
            .Throws<ArgumentException>();
        await Assert.That(() => (category with { Meaning = " " }).Validate(18)).Throws<ArgumentException>();
        await Assert.That(() => (category with { ReviewRationale = "" }).Validate(18)).Throws<ArgumentException>();
        await Assert.That(() => (category with { VersionId = Guid.NewGuid() }).Validate(18)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Mandate_RequiresSubstantiveGroundsAndIndependentControllers()
    {
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var mandate = new ModerationClassificationTaxonomyMandate(owner, owner, reviewer, reviewer,
            "Accountable coordinator owns this finite taxonomy.",
            "Qualified review of content meaning, child risk and audience floors.");
        mandate.Validate();
        await Assert.That(() => (mandate with { ReviewerControllerHumanId = owner }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (mandate with { OwnershipGrounds = "" }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (mandate with { ReviewMandateGrounds = "" }).Validate()).Throws<ArgumentException>();
    }

    internal static ModerationClassificationTaxonomyCategoryTerms Category(int id, string code, int floor) =>
        new(Guid.CreateVersion7(), id, code, EventContentClassificationVersion.CategoryKind.General,
            floor, "Reviewed category meaning.", "Explicit reviewed audience floor.");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Projection_RejectsReplacementAgeSourceOrAdoption(bool replaceSource)
    {
        var state = new TaxonomyState();
        var age = replaceSource ? state.RecordAge() : state.Age;
        var adoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), age,
            state.Reviewer, 3, TaxonomyState.Now, TaxonomyState.Now.AddMinutes(9));

        await Assert.That(() => state.Taxonomy.Project(age, adoption)).Throws<InvalidOperationException>();
        var projected = state.Taxonomy.Project(state.Age, state.AgeAdoption).Single();
        await Assert.That(projected.SourceId).IsEqualTo(state.Age.Id);
        await Assert.That(projected.AdoptionId).IsEqualTo(state.AgeAdoption.Id);
    }

    private sealed class TaxonomyState
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        private readonly Guid _owner = Guid.CreateVersion7();
        private readonly Guid _recorder = Guid.CreateVersion7();
        public Guid Reviewer { get; } = Guid.CreateVersion7();
        public ModerationChildAgePolicySource Age { get; }
        public ModerationChildAgePolicyAdoption AgeAdoption { get; }
        public ModerationClassificationTaxonomySource Taxonomy { get; }

        public TaxonomyState()
        {
            Age = RecordAge();
            AgeAdoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Age, Reviewer, 2, Now, Now.AddMinutes(9));
            var fallback = Guid.CreateVersion7();
            var governance = ModerationConstitutionSource.Record(Guid.CreateVersion7(), Age.HostBindingId, Age.TrustDomainId,
                Age.TenantId, Age.PolicyId, 1, Age.PublicationId, Age.DefinitionId, Age.ProfileId, 1, Age.ProposedStateDigest,
                new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                    ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                    ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                    ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
                    ModerationConstitutionOverload.AcknowledgeAndEscalate, 2, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
                    "https://example.invalid/support", "https://example.invalid/redress", _owner, fallback),
                [new(Guid.CreateVersion7(), 1, 3, _owner, _owner, "Canonical owner.", "Qualified content review.", "Independent mandate."),
                 new(Guid.CreateVersion7(), 1, 4, Reviewer, Reviewer, "Canonical reviewer.", "Qualified child review.", "Independent mandate.")],
                _recorder, 5, Now, Now.AddMinutes(8));
            var governanceAdoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), governance, fallback, 6, Now, Now.AddMinutes(7));
            Taxonomy = ModerationClassificationTaxonomySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 0,
                Age, AgeAdoption, governance, governanceAdoption,
                new(_owner, _owner, Reviewer, Reviewer, "Accountable taxonomy ownership.", "Independent category review."),
                [Category(1, "general", 6)], 7, Now, Now.AddMinutes(6));
        }

        public ModerationChildAgePolicySource RecordAge() =>
            ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), 1, new string('a', 64),
                new(ModerationScopeKind.Tenant,
                    ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
                    ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
                    ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
                    ModerationChildAgeProviderAssurance.NotAccepted,
                    ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
                    ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
                    ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
                    ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
                    ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
                    ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
                    ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
                    ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
                    ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
                    ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
                    "Child risk review.", "Privacy review.", "Actual subject collection purpose.",
                    "Actual subject consent.", "Qualified local threshold review.", null,
                    "Adopted operator Debate floor.", "Reviewed UTC Gregorian calendar."),
                new(Reviewer, Reviewer, _owner, _owner, _recorder, "Distinct controllers.", "Privacy mandate.",
                    "Child safeguarding mandate.", "Collection mandate."), _recorder, 1, Now, Now.AddMinutes(10));
    }
}
