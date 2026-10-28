using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationClassificationControlSelectionTests
{
    [Test]
    public async Task SelectionRetainsAcceptedReviewTimeAndIndependentCompleteBindings()
    {
        var state = new ClassificationControlState();
        var bindings = state.Bindings().ToList();
        var selection = state.Select(bindings);
        bindings.Clear();
        selection.ValidatePersistedState();
        await Assert.That(selection.StartsAt).IsEqualTo(state.Review.AcceptedAt);
        await Assert.That(selection.RecordedAt).IsEqualTo(ClassificationControlState.Now.AddSeconds(1));
        await Assert.That(selection.SourceId).IsEqualTo(state.Source.Id);
        await Assert.That(selection.SourceWriterGeneration).IsEqualTo(9L);
        await Assert.That(selection.ReviewWriterGeneration).IsEqualTo(10L);
        await Assert.That(selection.GenerationId).IsEqualTo(state.Generation.Id);
        await Assert.That(selection.GenerationWriterGeneration).IsEqualTo(3L);
        await Assert.That(selection.Bindings.Count).IsEqualTo(4);
        await Assert.That(selection.Bindings[2].Strategy).IsNull();
        await Assert.That(selection.Bindings[3].Strategy).IsEqualTo(ModerationAdditiveStrategyKind.ClassificationIntegrity);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task SelectionRejectsOwnerReviewerAndTheirHumanControllers(int actor)
    {
        var state = new ClassificationControlState();
        var human = actor switch
        {
            0 => state.Source.OwnerHumanId,
            1 => state.Source.ReviewerHumanId,
            2 => state.Source.OwnerControllerHumanId,
            _ => state.Source.ReviewerControllerHumanId
        };
        await Assert.That(() => state.Select(author: human)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("")]
    [Arguments("ABCDEF")]
    [Arguments("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaG")]
    public async Task SelectionRejectsNonCanonicalDigest(string digest)
    {
        var state = new ClassificationControlState();
        await Assert.That(() => state.Select(digest: digest)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task SelectionRejectsIncompleteTransplantedOrSanctionTimedBindings(int change)
    {
        var state = new ClassificationControlState();
        var bindings = state.Bindings().ToArray();
        if (change == 0) bindings = bindings.Where(row => row.Kind == ModerationContributionBindingKind.Effect).ToArray();
        if (change == 1) bindings[1] = state.Binding(0);
        if (change == 2) bindings[0] = state.Binding(0, selectionId: Guid.CreateVersion7());
        if (change == 3) bindings[0] = state.Binding(0, subjectKind: ModerationSubjectKind.Human);
        if (change == 4) bindings[0] = state.Binding(0, scope: state.TenantScope);
        if (change == 5) bindings[0] = state.Binding(0, windowEnd: ClassificationControlState.Now.AddMinutes(1));
        await Assert.That(() => state.Select(bindings)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task SelectionRejectsExpiredFutureOrRegressingReceiptValidity(int change)
    {
        var state = new ClassificationControlState();
        await Assert.That(() => state.Select(
            now: change == 0 ? ClassificationControlState.Now.AddTicks(-1)
                : change == 1 ? state.Review.ValidUntil : ClassificationControlState.Now.AddSeconds(1),
            until: change == 2 ? state.Review.ValidUntil.AddTicks(1) : ClassificationControlState.Now.AddMinutes(2),
            writer: change == 3 ? 10 : 11)).Throws<ArgumentException>();
    }

    [Test]
    public async Task WithdrawnReviewCannotCreateOrKeepCurrentSelection()
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        state.Review.End(1, ModerationClassificationMaterialEnd.Withdrawn, Guid.CreateVersion7(), 12,
            "Independent withdrawal of the accepted material.", ClassificationControlState.Now.AddSeconds(2));
        await Assert.That(() => state.Select()).Throws<ArgumentException>();
        await Assert.That(selection.MatchesCurrent(state.Source, state.Review, state.Generation,
            ClassificationControlState.Now.AddSeconds(3))).IsFalse();
    }

    [Test]
    public async Task ContentIdentifierEqualToAuthorDoesNotBecomeHumanSelfApproval()
    {
        var state = new ClassificationControlState();
        var selection = state.Select(author: state.Generation.EventId);
        await Assert.That(selection.AuthoredByHumanId).IsEqualTo(state.Generation.EventId);
        await Assert.That(selection.Bindings[0].SubjectKind).IsEqualTo(ModerationSubjectKind.Content);
    }

    [Test]
    public async Task ReobservedNativeEventCannotReplaceSelectedGeneration()
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        await Assert.That(() => state.Select(generation: state.Reobserve())).Throws<ArgumentException>();
        await Assert.That(selection.MatchesCurrent(state.Source, state.Review, state.Reobserve(),
            ClassificationControlState.Now.AddSeconds(2))).IsFalse();
        await Assert.That(selection.MatchesCurrent(state.Source, state.Review, state.Generation,
            selection.ValidUntil)).IsFalse();
    }
}

internal sealed class ClassificationControlState
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    internal Guid SelectionId { get; } = Guid.CreateVersion7();
    internal Guid Author { get; } = Guid.CreateVersion7();
    internal ModerationClassificationMaterialSource Source { get; }
    internal ModerationClassificationMaterialReview Review { get; }
    internal ModerationEventGeneration Generation { get; }
    internal AuthorityScope TenantScope { get; }

    internal ClassificationControlState()
    {
        var owner = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var recorder = Guid.NewGuid();
        var fallback = Guid.NewGuid();
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
        var age = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, new string('a', 64), terms,
            new(reviewer, reviewer, owner, owner, recorder, "Distinct controllers.", "Privacy mandate.",
                "Child safeguarding mandate.", "Collection mandate."), recorder, 1, Now, Now.AddMinutes(10));
        var ageAdoption = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), age, reviewer, 2, Now, Now.AddMinutes(9));
        var constitution = ModerationConstitutionSource.Record(Guid.CreateVersion7(), age.HostBindingId, age.TrustDomainId,
            age.TenantId, age.PolicyId, 1, age.PublicationId, age.DefinitionId, age.ProfileId, 1, age.ProposedStateDigest,
            new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
                ModerationConstitutionOverload.AcknowledgeAndEscalate, 2, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
                "https://example.invalid/support", "https://example.invalid/redress", owner, fallback),
            [new(Guid.CreateVersion7(), 1, 3, owner, owner, "Canonical owner.", "Qualified content review.", "Independent mandate."),
             new(Guid.CreateVersion7(), 1, 4, reviewer, reviewer, "Canonical reviewer.", "Qualified child review.", "Independent mandate.")],
            recorder, 5, Now, Now.AddMinutes(8));
        var constitutionAdoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), constitution, fallback, 6, Now, Now.AddMinutes(7));
        var taxonomy = ModerationClassificationTaxonomySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 0,
            age, ageAdoption, constitution, constitutionAdoption,
            new(owner, owner, reviewer, reviewer, "Accountable taxonomy ownership.", "Independent category review."),
            [new(Guid.CreateVersion7(), 1, "debate", EventContentClassificationVersion.CategoryKind.Debate, 18,
                "Reviewed Debate material.", "Adopted operator floor.")], 7, Now, Now.AddMinutes(6));
        var taxonomyAdoption = ModerationClassificationTaxonomyAdoption.Adopt(Guid.CreateVersion7(), taxonomy, reviewer, 8, Now, Now.AddMinutes(5));
        Generation = new()
        {
            Id = Guid.CreateVersion7(),
            HostBindingId = age.HostBindingId,
            TrustDomainId = age.TrustDomainId,
            TenantId = age.TenantId,
            EventId = Guid.NewGuid(),
            EventStamp = Guid.NewGuid(),
            ActorId = Guid.CreateVersion7(),
            ActorStamp = Guid.NewGuid(),
            OrganizerActorId = null,
            OrganizerActorStamp = null,
            SubmittedByHumanId = owner,
            ObservedByHumanId = recorder,
            Revision = 1,
            WriterGeneration = 3,
            ObservedAt = Now,
            ValidUntil = Now.AddMinutes(8)
        };
        Source = ModerationClassificationMaterialSource.Record(Guid.CreateVersion7(), 0, Generation, taxonomy, taxonomyAdoption,
            new(owner, owner, Guid.CreateVersion7(), 1, reviewer, reviewer,
                "Owner-qualified complete Event proposal.", "Independent review of exact forms, purpose and material."),
            [new(Guid.CreateVersion7(), new(Generation.EventId, Generation.EventStamp, null, null, null, null), [taxonomy.Categories[0].Id])],
            [new(ModerationClassificationMaterialComponentKind.Event, Generation.EventId, Generation.EventStamp, new string('a', 64))],
            9, Now, Now.AddMinutes(4));
        Review = ModerationClassificationMaterialReview.Accept(Guid.CreateVersion7(), Source, reviewer, 10, Now, Now.AddMinutes(3));
        TenantScope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(age.TrustDomainId), Guid.CreateVersion7()),
            Guid.CreateVersion7()), age.TenantId);
    }

    internal ModerationClassificationControlSelection Select(IEnumerable<ModerationContributionBinding>? bindings = null,
        Guid? author = null, string? digest = null, DateTimeOffset? now = null, DateTimeOffset? until = null,
        long writer = 11, ModerationEventGeneration? generation = null) =>
        ModerationClassificationControlSelection.Record(SelectionId, Source, Review, generation ?? Generation,
            author ?? Author, bindings ?? Bindings(), digest ?? new string('a', 64), writer,
            now ?? Now.AddSeconds(1), until ?? Now.AddMinutes(2));

    internal IEnumerable<ModerationContributionBinding> Bindings() => Enumerable.Range(0, 4).Select(ordinal => Binding(ordinal));

    internal ModerationContributionBinding Binding(int ordinal, Guid? selectionId = null,
        ModerationSubjectKind subjectKind = ModerationSubjectKind.Content, AuthorityScope? scope = null,
        DateTimeOffset? windowEnd = null) => new()
        {
            SelectionId = selectionId ?? SelectionId,
            Ordinal = ordinal,
            Kind = ordinal < 2 ? ModerationContributionBindingKind.Effect : ModerationContributionBindingKind.Consequence,
            EffectKind = ordinal == 0 ? ModerationReferenceEffectKind.ClassificationAndMaterialApproval
            : ordinal == 1 ? ModerationReferenceEffectKind.AudienceDisclosureAndDerivatives : null,
            EffectMode = ordinal == 0 ? ModerationReferenceEffectMode.RequireReview : ordinal == 1 ? ModerationReferenceEffectMode.Guard : null,
            EffectTarget = ordinal < 2 ? ModerationReferenceTarget.ClassifiedContentVersions : null,
            Capability = ordinal == 0 ? ModerationCapability.Publish : ordinal == 1 ? ModerationCapability.ReadResource : null,
            Execution = ordinal < 2 ? ModerationReferenceExecution.NativeOwner : null,
            Strategy = ordinal == 3 ? ModerationAdditiveStrategyKind.ClassificationIntegrity : null,
            Scope = scope ?? AuthorityScope.Event(TenantScope, Generation.EventId),
            SubjectKind = subjectKind,
            SubjectId = Generation.EventId,
            TargetId = Generation.EventId,
            TargetRevision = Generation.Revision,
            Operation = ordinal == 0 ? "classification.approval" : "event.disclosure",
            WindowEnd = windowEnd
        };

    internal ModerationEventGeneration Reobserve() => new()
    {
        Id = Guid.CreateVersion7(),
        HostBindingId = Generation.HostBindingId,
        TrustDomainId = Generation.TrustDomainId,
        TenantId = Generation.TenantId,
        EventId = Generation.EventId,
        EventStamp = Generation.EventStamp,
        ActorId = Generation.ActorId,
        ActorStamp = Generation.ActorStamp,
        OrganizerActorId = Generation.OrganizerActorId,
        OrganizerActorStamp = Generation.OrganizerActorStamp,
        SubmittedByHumanId = Generation.SubmittedByHumanId,
        ObservedByHumanId = Generation.ObservedByHumanId,
        Revision = Generation.Revision,
        WriterGeneration = 12,
        ObservedAt = Now,
        ValidUntil = Generation.ValidUntil
    };
}
