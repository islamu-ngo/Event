using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

/// <summary>Structural review invariants, not witnesses of native taxonomy or reviewer authority.</summary>
public sealed class ModerationClassificationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Review_SnapshotsIndependentCategoriesAndDeclaredChildren()
    {
        var context = new Context();
        var general = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
        var mature = context.Category(2, EventContentClassificationVersion.CategoryKind.Mature, 16);
        var categories = new List<EventContentClassificationVersion> { general, mature };
        var child = context.Material with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        var children = new List<ModerationClassificationMaterial> { child };
        var review = context.Review(categories, children);
        categories.Clear();
        children.Clear();
        await Assert.That(review.Categories.Count).IsEqualTo(2);
        await Assert.That(review.Categories[1].MinimumAge).IsEqualTo(16);
        await Assert.That(review.RequiredChildren.Single()).IsEqualTo(child);
        await Assert.That(review.MatchesCurrent(context.Generation, context.Material, [general, mature],
            [child], context.Source, context.Adoption, Now)).IsTrue();
        await Assert.That(review.MatchesCurrent(context.Generation, context.Material, [general],
            [child], context.Source, context.Adoption, Now)).IsFalse();
    }

    [Test]
    public async Task Review_RejectsDuplicateCategoryRevisions_AndCrossParentChildren()
    {
        var context = new Context();
        var first = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
        var second = context.Category(1, EventContentClassificationVersion.CategoryKind.Mature, 16, revision: 2);
        await Assert.That(() => context.Review([first, second], [])).Throws<ArgumentException>();
        var foreign = context.Material with { EventId = Guid.NewGuid(), SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        await Assert.That(() => context.Review([first], [foreign])).Throws<ArgumentException>();
        await Assert.That(() => context.Review([first], [context.Material])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Review_RequiresIndependentHumansAndControllers_NotJustDifferentAccounts()
    {
        var context = new Context();
        var category = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
        await Assert.That(() => context.Review([category], [], reviewer: context.Owner)).Throws<ArgumentException>();
        await Assert.That(() => context.Review([category], [], reviewerController: context.Owner)).Throws<ArgumentException>();
        await Assert.That(() => context.Category(2, EventContentClassificationVersion.CategoryKind.Mature, 16,
            reviewerController: context.Owner)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TerminalCorrectionOrWithdrawal_NeverRevivesHistoricReview(bool correction)
    {
        var context = new Context();
        var category = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
        var review = context.Review([category], []);
        if (correction) review.Correct(1, context.Reviewer, 7, Now.AddMinutes(1));
        else review.Withdraw(1, context.Reviewer, 7, Now.AddMinutes(1));
        await Assert.That(review.Revision).IsEqualTo(2);
        await Assert.That(review.State).IsEqualTo(correction
            ? EventContentClassification.ReviewState.Corrected : EventContentClassification.ReviewState.Withdrawn);
        await Assert.That(review.Categories.Single()).IsEqualTo(category);
        await Assert.That(review.MatchesCurrent(context.Generation, context.Material, [category], [],
            context.Source, context.Adoption, Now)).IsFalse();
        await Assert.That(() => review.Withdraw(2, context.Reviewer, 8, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExpiryAndStaleRevision_CannotRenewOrMutateAcceptedReview()
    {
        var context = new Context();
        var category = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
        var review = context.Review([category], []);
        await Assert.That(review.MatchesCurrent(context.Generation, context.Material, [category], [],
            context.Source, context.Adoption, review.ValidUntil)).IsFalse();
        await Assert.That(() => review.Correct(2, context.Reviewer, 7, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => review.Correct(1, context.Reviewer, 6, Now)).Throws<ArgumentException>();
        await Assert.That(() => context.Review([category], [], validUntil: category.ValidUntil.AddTicks(1)))
            .Throws<ArgumentException>();
        review.Withdraw(1, context.Reviewer, 7, Now.AddDays(1));
        await Assert.That(review.ReviewedAt).IsEqualTo(Now);
        await Assert.That(review.ValidUntil).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    public async Task MaterialCoordinates_RequirePairedNativeStampsAndExactSessionParent()
    {
        var context = new Context();
        var session = context.Material with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        var resource = session with { ResourceId = Guid.NewGuid(), ResourceStamp = Guid.NewGuid() };
        await Assert.That(resource.Parent).IsEqualTo(session);
        await Assert.That(session.Parent).IsEqualTo(context.Material);
        await Assert.That(() => (session with { SessionStamp = null }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (resource with { ResourceId = null }).Validate()).Throws<ArgumentException>();
        await Assert.That(() => (context.Material with { EventStamp = Guid.Empty }).Validate()).Throws<ArgumentException>();
    }

    [Test]
    public async Task CategoryMeaning_RequiresExplicitBoundedFloorAndFiniteVersionedReview()
    {
        var context = new Context();
        await Assert.That(() => context.Category(1, EventContentClassificationVersion.CategoryKind.General, -1))
            .Throws<ArgumentException>();
        await Assert.That(() => context.Category(1, EventContentClassificationVersion.CategoryKind.Mature, 131))
            .Throws<ArgumentException>();
        await Assert.That(() => context.Category(0, EventContentClassificationVersion.CategoryKind.General, 0))
            .Throws<ArgumentException>();
        await Assert.That(() => context.Category(1, (EventContentClassificationVersion.CategoryKind)99, 0))
            .Throws<ArgumentException>();
        var category = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 0);
        await Assert.That(category.MatchesPolicy(context.Source, context.Adoption, Now)).IsTrue();
        context.Adoption.Withdraw(1, context.Reviewer, Now.AddMinutes(1));
        await Assert.That(category.MatchesPolicy(context.Source, context.Adoption, Now)).IsFalse();
    }

    private sealed class Context
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Reviewer { get; } = Guid.NewGuid();
        public Guid TaxonomyId { get; } = Guid.CreateVersion7();
        public ModerationChildAgePolicySource Source { get; }
        public ModerationChildAgePolicyAdoption Adoption { get; }
        public ModerationEventGeneration Generation { get; }
        public ModerationClassificationMaterial Material => new(Generation.EventId, Generation.EventStamp, null, null, null, null);

        public Context()
        {
            var policyReviewer = Guid.NewGuid();
            var collectionOwner = Guid.NewGuid();
            var recorder = Guid.NewGuid();
            Source = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), 1, new string('a', 64), Terms(),
                new(policyReviewer, policyReviewer, collectionOwner, collectionOwner, recorder,
                    "Independent canonical controllers.", "Privacy mandate.", "Child safeguarding mandate.", "Collection mandate."),
                recorder, 1, Now, Now.AddMinutes(10));
            Adoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Source, policyReviewer, 2, Now, Now.AddMinutes(9));
            Generation = new()
            {
                Id = Guid.CreateVersion7(), HostBindingId = Source.HostBindingId, TrustDomainId = Source.TrustDomainId,
                TenantId = Source.TenantId, EventId = Guid.NewGuid(), EventStamp = Guid.NewGuid(), Revision = 1,
                ObservedByHumanId = Owner, WriterGeneration = 3, ObservedAt = Now, ValidUntil = Now.AddMinutes(8),
                ActorId = Guid.NewGuid(), ActorStamp = Guid.NewGuid(), OrganizerActorId = null,
                OrganizerActorStamp = null, SubmittedByHumanId = Owner
            };
        }

        public EventContentClassificationVersion Category(int id, EventContentClassificationVersion.CategoryKind kind,
            int floor, long revision = 1, Guid? reviewerController = null) =>
            EventContentClassificationVersion.Record(Guid.CreateVersion7(), TaxonomyId, id, revision, $"category-{id}",
                kind, floor, "Explicit audience category meaning.", "Reviewed audience rationale.",
                Source, Adoption, Owner, Owner, Reviewer, reviewerController ?? Reviewer, 4, Now, Now.AddMinutes(7));

        public EventContentClassification Review(IEnumerable<EventContentClassificationVersion> categories,
            IEnumerable<ModerationClassificationMaterial> children, Guid? reviewer = null,
            Guid? reviewerController = null, DateTimeOffset? validUntil = null) =>
            EventContentClassification.Review(Guid.CreateVersion7(), Generation, Material, categories, children,
                Source, Adoption, Owner, Owner, reviewer ?? Reviewer, reviewerController ?? Reviewer,
                6, Now, validUntil ?? Now.AddMinutes(5));
    }

    private static ModerationChildAgePolicyTerms Terms() => new(
        ModerationScopeKind.Tenant, ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
        ModerationChildAgeProviderAssurance.NotAccepted, ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
        ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
        ModerationChildAgeLeapDay.MarchFirstInNonLeapYear, ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes,
        18, "Child risk review.", "Privacy review.", "Subject-bound collection purpose.",
        "Actual subject prerequisites.", "Qualified local threshold review.", null,
        "ISLAMU operator Debate floor, not universal legislation.", "Reviewed UTC Gregorian March 1 semantics.");
}
