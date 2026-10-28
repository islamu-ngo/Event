using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationClassificationMaterialTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Proposal_SnapshotsExactMaterialInventoryAndCategoryVersions()
    {
        var fixture = new MaterialState();
        var selections = fixture.Selections.ToList();
        var components = fixture.Components.ToList();
        var source = fixture.Record(selections, components);
        selections.Clear();
        components.Clear();
        await Assert.That(source.Materials.Count).IsEqualTo(3);
        await Assert.That(source.Components.Count).IsEqualTo(3);
        await Assert.That(source.MatchesCurrent(fixture.Generation, fixture.Taxonomy, fixture.TaxonomyAdoption,
            fixture.Selections.Select(row => row.Material), fixture.Components, Now)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Currentness_RejectsOmittedChangedOrSubstitutedNativeChildren(int change)
    {
        var fixture = new MaterialState();
        var source = fixture.Record();
        var materials = fixture.Selections.Select(row => row.Material).ToArray();
        var components = fixture.Components.ToArray();
        if (change == 0) materials = materials[..^1];
        if (change == 1) materials[2] = materials[2] with { ResourceStamp = Guid.NewGuid() };
        if (change == 2) materials[2] = materials[2] with { SessionId = null, SessionStamp = null };
        if (change == 3) components[2] = components[2] with { Digest = new string('b', 64) };
        if (change == 4) components = components[..^1];
        await Assert.That(source.MatchesCurrent(fixture.Generation, fixture.Taxonomy, fixture.TaxonomyAdoption,
            materials, components, Now)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Proposal_RejectsDisconnectedDuplicateOrUnknownCategorySelection(int change)
    {
        var fixture = new MaterialState();
        var selections = fixture.Selections.ToArray();
        if (change == 0) selections = selections[1..];
        if (change == 1) selections[2] = selections[1];
        if (change == 2) selections[1] = selections[1] with { CategoryVersionIds = [Guid.CreateVersion7()] };
        if (change == 3) selections[1] = selections[1] with { CategoryVersionIds = [] };
        await Assert.That(() => fixture.Record(selections)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Acceptance_RequiresNominatedIndependentReviewerAndFiniteOriginalWindow()
    {
        var fixture = new MaterialState();
        var source = fixture.Record();
        await Assert.That(() => ModerationClassificationMaterialReview.Accept(Guid.CreateVersion7(), source,
            fixture.Owner, 10, Now, Now.AddMinutes(2))).Throws<ArgumentException>();
        await Assert.That(() => ModerationClassificationMaterialReview.Accept(Guid.CreateVersion7(), source,
            fixture.Reviewer, 10, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        var review = fixture.Accept(source);
        await Assert.That(review.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(review.MatchesCurrent(source, review.ValidUntil)).IsFalse();
        await Assert.That(source.WriterGeneration).IsEqualTo(9L);
    }

    [Test]
    [Arguments(ModerationClassificationMaterialEnd.Corrected)]
    [Arguments(ModerationClassificationMaterialEnd.Withdrawn)]
    public async Task AccountablePostExpiryEnding_IsTerminalWithoutRewritingBirth(ModerationClassificationMaterialEnd ending)
    {
        var fixture = new MaterialState();
        var source = fixture.Record();
        var review = fixture.Accept(source);
        var corrector = Guid.CreateVersion7();
        review.End(1, ending, corrector, 11, "Accountable correction of retained material review.", Now.AddDays(1));
        await Assert.That(review.Revision).IsEqualTo(2L);
        await Assert.That(review.ReviewerHumanId).IsEqualTo(fixture.Reviewer);
        await Assert.That(review.WriterGeneration).IsEqualTo(10L);
        await Assert.That(review.EndWriterGeneration).IsEqualTo(11L);
        await Assert.That(review.EndedByHumanId).IsEqualTo(corrector);
        await Assert.That(review.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => review.End(2, ending, corrector, 12, "A second ending is forbidden.", Now.AddDays(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CurrentTaxonomyWithdrawalAndCategorySubstitution_CannotReuseReview()
    {
        var fixture = new MaterialState();
        var source = fixture.Record();
        fixture.TaxonomyAdoption.Withdraw(1, fixture.Reviewer, Now.AddMinutes(1));
        await Assert.That(source.MatchesCurrent(fixture.Generation, fixture.Taxonomy, fixture.TaxonomyAdoption,
            fixture.Selections.Select(row => row.Material), fixture.Components, Now)).IsFalse();
    }

    [Test]
    public async Task Projection_UsesEveryImmediateChildWithoutCreatingAgeOrAudiencePermission()
    {
        var fixture = new MaterialState();
        var source = fixture.Record();
        var reviews = source.Project(fixture.Accept(source), fixture.Generation, fixture.Taxonomy,
            fixture.TaxonomyAdoption, fixture.Age, fixture.AgeAdoption, Now);
        await Assert.That(reviews.Length).IsEqualTo(3);
        await Assert.That(reviews[0].RequiredChildren.Single()).IsEqualTo(fixture.Selections[1].Material);
        await Assert.That(reviews[1].RequiredChildren.Single()).IsEqualTo(fixture.Selections[2].Material);
        await Assert.That(reviews[2].Categories.Single().MinimumAge).IsEqualTo(18);
    }

    [Test]
    public async Task Mandate_RejectsPublisherControllerAndEmptyReviewGrounds()
    {
        var fixture = new MaterialState();
        await Assert.That(() => (fixture.Mandate with { ReviewerControllerHumanId = fixture.Owner }).Validate())
            .Throws<ArgumentException>();
        await Assert.That(() => (fixture.Mandate with { ReviewGrounds = "" }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (fixture.Components[0] with { NativeStamp = Guid.Empty }).Validate())
            .Throws<ArgumentException>();
    }

    private sealed class MaterialState
    {
        public Guid Owner { get; } = Guid.CreateVersion7();
        public Guid Reviewer { get; } = Guid.CreateVersion7();
        public ModerationChildAgePolicySource Age { get; }
        public ModerationChildAgePolicyAdoption AgeAdoption { get; }
        public ModerationClassificationTaxonomySource Taxonomy { get; }
        public ModerationClassificationTaxonomyAdoption TaxonomyAdoption { get; }
        public ModerationEventGeneration Generation { get; }
        public ModerationClassificationMaterialMandate Mandate { get; }
        public ImmutableArray<ModerationClassificationMaterialSelection> Selections { get; }
        public ImmutableArray<ModerationClassificationMaterialComponentVersion> Components { get; }

        public MaterialState()
        {
            var recorder = Guid.CreateVersion7();
            var fallback = Guid.CreateVersion7();
            var terms = new ModerationChildAgePolicyTerms(ModerationScopeKind.Tenant,
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
                "Adopted operator Debate floor.", "Reviewed UTC Gregorian calendar.");
            Age = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), 1, new string('a', 64), terms,
                new(Reviewer, Reviewer, Owner, Owner, recorder, "Distinct controllers.", "Privacy mandate.",
                    "Child safeguarding mandate.", "Collection mandate."), recorder, 1, Now, Now.AddMinutes(10));
            AgeAdoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Age, Reviewer, 2, Now, Now.AddMinutes(9));
            var governance = ModerationConstitutionSource.Record(Guid.CreateVersion7(), Age.HostBindingId, Age.TrustDomainId,
                Age.TenantId, Age.PolicyId, 1, Age.PublicationId, Age.DefinitionId, Age.ProfileId, 1, Age.ProposedStateDigest,
                new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                    ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                    ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                    ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
                    ModerationConstitutionOverload.AcknowledgeAndEscalate, 2, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
                    "https://example.invalid/support", "https://example.invalid/redress", Owner, fallback),
                [new(Guid.CreateVersion7(), 1, 3, Owner, Owner, "Canonical owner.", "Qualified content review.", "Independent mandate."),
                 new(Guid.CreateVersion7(), 1, 4, Reviewer, Reviewer, "Canonical reviewer.", "Qualified child review.", "Independent mandate.")],
                recorder, 5, Now, Now.AddMinutes(8));
            var governanceAdoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), governance, fallback, 6, Now, Now.AddMinutes(7));
            Taxonomy = ModerationClassificationTaxonomySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 0,
                Age, AgeAdoption, governance, governanceAdoption,
                new(Owner, Owner, Reviewer, Reviewer, "Accountable taxonomy ownership.", "Independent category review."),
                [new(Guid.CreateVersion7(), 1, "debate", EventContentClassificationVersion.CategoryKind.Debate, 18,
                    "Reviewed Debate material.", "Adopted operator floor.")], 7, Now, Now.AddMinutes(6));
            TaxonomyAdoption = ModerationClassificationTaxonomyAdoption.Adopt(Guid.CreateVersion7(), Taxonomy, Reviewer, 8, Now, Now.AddMinutes(5));
            Generation = new()
            {
                Id = Guid.CreateVersion7(),
                HostBindingId = Age.HostBindingId,
                TrustDomainId = Age.TrustDomainId,
                TenantId = Age.TenantId,
                EventId = Guid.CreateVersion7(),
                EventStamp = Guid.NewGuid(),
                ActorId = Guid.CreateVersion7(),
                ActorStamp = Guid.NewGuid(),
                OrganizerActorId = null,
                OrganizerActorStamp = null,
                SubmittedByHumanId = Owner,
                ObservedByHumanId = recorder,
                Revision = 1,
                WriterGeneration = 3,
                ObservedAt = Now,
                ValidUntil = Now.AddMinutes(8)
            };
            Mandate = new(Owner, Owner, Guid.CreateVersion7(), 1, Reviewer, Reviewer,
                "Owner-qualified complete Event proposal.", "Independent review of exact forms, purpose and material.");
            var root = new ModerationClassificationMaterial(Generation.EventId, Generation.EventStamp, null, null, null, null);
            var session = root with { SessionId = Guid.CreateVersion7(), SessionStamp = Guid.NewGuid() };
            var resource = session with { ResourceId = Guid.CreateVersion7(), ResourceStamp = Guid.NewGuid() };
            Selections = [new(Guid.CreateVersion7(), root, [Taxonomy.Categories[0].Id]),
                new(Guid.CreateVersion7(), session, [Taxonomy.Categories[0].Id]),
                new(Guid.CreateVersion7(), resource, [Taxonomy.Categories[0].Id])];
            Components = [new(ModerationClassificationMaterialComponentKind.Event, root.EventId, root.EventStamp, new string('a', 64)),
                new(ModerationClassificationMaterialComponentKind.Session, session.SessionId!.Value, session.SessionStamp, new string('a', 64)),
                new(ModerationClassificationMaterialComponentKind.Resource, resource.ResourceId!.Value, resource.ResourceStamp, new string('a', 64))];
        }

        public ModerationClassificationMaterialSource Record(
            IEnumerable<ModerationClassificationMaterialSelection>? selections = null,
            IEnumerable<ModerationClassificationMaterialComponentVersion>? components = null) =>
            ModerationClassificationMaterialSource.Record(Guid.CreateVersion7(), 0, Generation, Taxonomy, TaxonomyAdoption,
                Mandate, selections ?? Selections, components ?? Components, 9, Now, Now.AddMinutes(4));

        public ModerationClassificationMaterialReview Accept(ModerationClassificationMaterialSource source) =>
            ModerationClassificationMaterialReview.Accept(Guid.CreateVersion7(), source, Reviewer, 10, Now, Now.AddMinutes(3));
    }
}
