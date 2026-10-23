using Explore.Application.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Pure metadata compilation; no case here claims native authorization or activation.</summary>
public sealed class ModerationClassificationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DebateFloor_IsIndependentOfFormat_WhileMaterialChangesRequireFreshReview()
    {
        var context = new Context();
        var native = NativeEvent(context.Generation);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(6);
        var debate = context.Category(2, EventContentClassificationVersion.CategoryKind.Debate, 0);
        context.Taxonomy.Add(debate);
        context.Materials[context.Root] = [debate];
        context.ReviewAll();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(18);
        native.EventType = new() { Id = 12, MasterCode = "workshop", FullName = "Workshop" };
        native.EventTypeId = native.EventType.Id;
        native.Title = "General workshop";
        native.ConcurrencyStamp = Guid.NewGuid();
        var previous = context.Generation;
        context.Generation = new()
        {
            Id = Guid.CreateVersion7(), HostBindingId = previous.HostBindingId, TrustDomainId = previous.TrustDomainId,
            TenantId = previous.TenantId, EventId = native.Id, EventStamp = native.ConcurrencyStamp, Revision = 2,
            ObservedByHumanId = previous.ObservedByHumanId, WriterGeneration = 5, ObservedAt = Now,
            ValidUntil = previous.ValidUntil, ActorId = previous.ActorId, ActorStamp = previous.ActorStamp,
            OrganizerActorId = previous.OrganizerActorId, OrganizerActorStamp = previous.OrganizerActorStamp,
            SubmittedByHumanId = previous.SubmittedByHumanId
        };
        var material = new ModerationClassificationMaterial(native.Id, native.ConcurrencyStamp, null, null, null, null);
        context.Materials.Clear();
        context.Materials.Add(material, [debate]);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.ReviewAll();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(18);
        context.Materials[material] = [];
        await Assert.That(context.Compile().Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.ReviewRequired);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
    }

    [Test]
    public async Task GeneralAndMatureFloors_ComeFromReviewedVersions_NotDebateDefault()
    {
        var context = new Context();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(6);
        var mature = context.Category(2, EventContentClassificationVersion.CategoryKind.Mature, 16);
        context.Taxonomy.Add(mature);
        context.Materials[context.Root] = [context.General, mature];
        await Assert.That(context.Compile().Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.ReviewRequired);
        context.ReviewAll();
        var result = context.Compile();
        await Assert.That(result.Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.CurrentMetadata);
        await Assert.That(result.WholeEventMinimumAge).IsEqualTo(16);
        await Assert.That(result.ValidUntil).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    public async Task StricterResourceRestrictsEntireEvent_AndWeakerChildrenCannotLowerParent()
    {
        var context = new Context();
        var mature = context.Category(2, EventContentClassificationVersion.CategoryKind.Mature, 21);
        context.Taxonomy.Add(mature);
        var session = context.Root with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        var resource = session with { ResourceId = Guid.NewGuid(), ResourceStamp = Guid.NewGuid() };
        context.Materials.Add(session, [context.General]);
        context.Materials.Add(resource, [mature]);
        context.ReviewAll();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(21);
        context.Materials[context.Root] = [mature];
        context.Materials[resource] = [context.General];
        context.ReviewAll();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(21);
    }

    [Test]
    [Arguments("session")]
    [Arguments("resource")]
    [Arguments("parent")]
    [Arguments("omitted")]
    [Arguments("unknown")]
    public async Task ExactAncestryAndCompleteInventory_AreRequiredForEveryChild(string change)
    {
        var context = new Context();
        var session = context.Root with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        var resource = session with { ResourceId = Guid.NewGuid(), ResourceStamp = Guid.NewGuid() };
        context.Materials.Add(session, [context.General]);
        context.Materials.Add(resource, [context.General]);
        context.ReviewAll();
        await Assert.That(context.Compile().Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.CurrentMetadata);
        if (change == "session")
        {
            context.Materials.Remove(session);
            context.Materials.Add(session with { SessionStamp = Guid.NewGuid() }, [context.General]);
        }
        else if (change == "resource")
        {
            context.Materials.Remove(resource);
            context.Materials.Add(resource with { ResourceStamp = Guid.NewGuid() }, [context.General]);
        }
        else if (change == "parent")
        {
            context.Materials.Remove(resource);
            context.Materials.Add(resource with { SessionId = null, SessionStamp = null }, [context.General]);
        }
        else if (change == "omitted") context.Materials.Remove(resource);
        else context.Materials[resource] = [];
        var result = context.Compile();
        await Assert.That(result.Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.ReviewRequired);
        await Assert.That(result.WholeEventMinimumAge).IsNull();
    }

    [Test]
    public async Task UnreviewedNewChild_OrMissingChildReview_CannotExposeSafeParentSummary()
    {
        var context = new Context();
        var session = context.Root with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        context.Materials.Add(session, [context.General]);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.ReviewAll();
        context.Reviews.RemoveAll(review => review.Material == session);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.Reviews.Clear();
        await Assert.That(context.Compile().Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.ReviewRequired);
    }

    [Test]
    public async Task ChangedTaxonomyVersion_CannotReuseAcceptedCategoryReview()
    {
        var context = new Context();
        var next = context.Category(1, EventContentClassificationVersion.CategoryKind.General, 12, revision: 2);
        context.Taxonomy.Clear();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.Taxonomy.Add(next);
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.Materials[context.Root] = [next];
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.ReviewAll();
        await Assert.That(context.Compile().WholeEventMinimumAge).IsEqualTo(12);
    }

    [Test]
    public async Task ExpiredWithdrawnOrCorrectedReviews_AndWithdrawnPolicy_RemainReviewRequired()
    {
        var context = new Context();
        await Assert.That(context.Compile(Now.AddMinutes(5)).WholeEventMinimumAge).IsNull();
        context.Reviews[0].Correct(1, context.Reviewer, 7, Now.AddMinutes(1));
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.ReviewAll();
        context.Reviews[0].Withdraw(1, context.Reviewer, 7, Now.AddMinutes(1));
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
        context.ReviewAll();
        context.Adoption.Withdraw(1, context.Reviewer, Now.AddMinutes(1));
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
    }

    [Test]
    [Arguments("policy")]
    [Arguments("publication")]
    [Arguments("definition")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("source-generation")]
    [Arguments("adoption")]
    public async Task ChangedPolicyCoordinates_RequireNewCategoryAndMaterialReviews(string change)
    {
        var context = new Context();
        var old = context.Source;
        context.Source = ModerationChildAgePolicySource.Record(old.Id, old.HostBindingId,
            old.TrustDomainId, old.TenantId, old.PolicyId, change == "policy" ? 2 : old.PolicyRevision,
            change == "publication" ? Guid.CreateVersion7() : old.PublicationId,
            change == "definition" ? Guid.CreateVersion7() : old.DefinitionId,
            change == "profile" ? Guid.CreateVersion7() : old.ProfileId, change == "mode" ? 2 : old.ModeCode,
            old.ProposedStateDigest, old.Terms, old.Ownership, old.RecordedByHumanId,
            change == "source-generation" ? 7 : old.WriterGeneration, Now, Now.AddMinutes(10));
        context.Adoption = ModerationChildAgePolicyAdoption.Adopt(
            change == "adoption" ? Guid.CreateVersion7() : context.Adoption.Id, context.Source,
            context.Source.ReviewerHumanId, change == "source-generation" ? 8 : 2, Now, Now.AddMinutes(9));
        await Assert.That(context.Compile().WholeEventMinimumAge).IsNull();
    }

    [Test]
    [Arguments("event")]
    [Arguments("actor")]
    [Arguments("observation")]
    [Arguments("writer")]
    public async Task CurrentGenerationAndScope_MustMatchAcceptedObservationNotJustEventIdentity(string change)
    {
        var context = new Context();
        var old = context.Generation;
        var changed = new ModerationEventGeneration
        {
            Id = old.Id, HostBindingId = old.HostBindingId, TrustDomainId = old.TrustDomainId,
            TenantId = old.TenantId, EventId = old.EventId, EventStamp = change == "event" ? Guid.NewGuid() : old.EventStamp,
            Revision = old.Revision, ObservedByHumanId = old.ObservedByHumanId,
            WriterGeneration = change == "writer" ? 7 : old.WriterGeneration,
            ObservedAt = change == "observation" ? Now.AddTicks(-1) : Now,
            ValidUntil = old.ValidUntil, ActorId = old.ActorId, ActorStamp = change == "actor" ? Guid.NewGuid() : old.ActorStamp,
            OrganizerActorId = null, OrganizerActorStamp = null, SubmittedByHumanId = old.SubmittedByHumanId
        };
        var result = ModerationAudiencePolicyCompiler.Compile(changed, context.Materials,
            context.Taxonomy, context.Reviews, context.Source, context.Adoption, Now);
        await Assert.That(result.WholeEventMinimumAge).IsNull();
        var foreign = new Context();
        result = ModerationAudiencePolicyCompiler.Compile(old, context.Materials,
            context.Taxonomy, context.Reviews, foreign.Source, foreign.Adoption, Now);
        await Assert.That(result.WholeEventMinimumAge).IsNull();
    }

    private static Explore.Domain.Event NativeEvent(ModerationEventGeneration generation) => new()
    {
        Id = generation.EventId, ConcurrencyStamp = generation.EventStamp, Title = "Debate",
        Actor = new() { ActorType = new() { FullName = "Person", MasterCode = "person" }, Pii = new() { DisplayName = "Organizer" } },
        Tenant = new() { FullName = "Tenant", Slug = "tenant", TenantStatus = new() { FullName = "Active", MasterCode = "active" } },
        VisibilityType = new() { MasterCode = "public", FullName = "Public" }, EventStatus = new(),
        EventFormat = new() { MasterCode = "online", FullName = "Online" },
        EventType = new() { Id = 11, MasterCode = "conference", FullName = "Conference" }, EventTypeId = 11
    };

    private sealed class Context
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Reviewer { get; } = Guid.NewGuid();
        public Guid TaxonomyId { get; } = Guid.CreateVersion7();
        public ModerationChildAgePolicySource Source { get; set; }
        public ModerationChildAgePolicyAdoption Adoption { get; set; }
        public ModerationEventGeneration Generation { get; set; }
        public ModerationClassificationMaterial Root => new(Generation.EventId, Generation.EventStamp, null, null, null, null);
        public EventContentClassificationVersion General { get; }
        public Dictionary<ModerationClassificationMaterial, IReadOnlyCollection<EventContentClassificationVersion>> Materials { get; } = [];
        public List<EventContentClassificationVersion> Taxonomy { get; } = [];
        public List<EventContentClassification> Reviews { get; } = [];

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
            General = Category(1, EventContentClassificationVersion.CategoryKind.General, 6);
            Taxonomy.Add(General);
            Materials.Add(Root, [General]);
            ReviewAll();
        }

        public EventContentClassificationVersion Category(int id, EventContentClassificationVersion.CategoryKind kind,
            int floor, long revision = 1) =>
            EventContentClassificationVersion.Record(Guid.CreateVersion7(), TaxonomyId, id, revision, $"category-{id}",
                kind, floor, "Explicit audience category meaning.", "Reviewed audience rationale.",
                Source, Adoption, Owner, Owner, Reviewer, Reviewer, 4, Now, Now.AddMinutes(7));

        public void ReviewAll()
        {
            Reviews.Clear();
            foreach (var (material, categories) in Materials)
                Reviews.Add(EventContentClassification.Review(Guid.CreateVersion7(), Generation, material, categories,
                    Materials.Keys.Where(candidate => candidate.Parent == material), Source, Adoption,
                    Owner, Owner, Reviewer, Reviewer, 6, Now, Now.AddMinutes(5)));
        }

        public ModerationAudiencePolicyCompiler.Compilation Compile(DateTimeOffset? now = null) =>
            ModerationAudiencePolicyCompiler.Compile(Generation, Materials, Taxonomy, Reviews, Source, Adoption, now ?? Now);
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
