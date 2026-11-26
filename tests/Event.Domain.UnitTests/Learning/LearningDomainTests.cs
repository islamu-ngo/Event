using Explore.Domain.Learning;

namespace Event.Domain.UnitTests.Learning;

public sealed class LearningDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Recipes =
        ["event-details", "sessions-program", "ticket-capacity", "attendees-team", "publication-lifecycle", "ai-example-review"];

    private static Guid Id() => Guid.CreateVersion7();
    private static UserLearningState Learner() => UserLearningState.Create(Id(), Id(), Id(), Now);
    private static readonly string PlanFingerprint = new('a', 64);
    private static readonly string PolicySourceStamp = new('B', 64);
    private static LearningTourDefinition Tour(LearningTourFamily family = LearningTourFamily.Shell, int version = 2) =>
        new(family, version,
            [new("navigation", 1), new("workspace", 1), new("review", 1)]);

    private static void Start(UserLearningState state, Guid? claim = null, int version = 2,
        LearningTourFamily family = LearningTourFamily.Shell, bool replay = false, DateTime? now = null) =>
        state.ClaimSemanticTour(state.Tours.SingleOrDefault(t => t.Family == family && t.MaterialVersion == version)?.Id ?? Id(),
            Tour(family, version), claim ?? Id(), Id(), state.Revision, now ?? Now,
            PlanFingerprint, PolicySourceStamp, replay: replay);

    private static void TourAction(UserLearningState state, Guid? claim, LearningTourAction action,
        string? stepKey = null, int? stepVersion = null, Guid? intent = null, long? expected = null,
        DateTime? now = null, LearningTourDefinition? definition = null, Guid? progressId = null,
        LearningSnoozeDuration? snoozeDuration = null, string? planFingerprint = null, string? policySourceStamp = null)
    {
        definition ??= Tour();
        state.RecordSemanticTourAction(progressId ?? state.Tours.SingleOrDefault(t =>
                t.Family == definition.Family && t.MaterialVersion == definition.MaterialVersion)?.Id ?? Id(),
            definition, claim, intent ?? Id(), action, stepKey, stepVersion, snoozeDuration,
            planFingerprint ?? PlanFingerprint, policySourceStamp ?? PolicySourceStamp,
            expected ?? state.Revision, now ?? Now, state.ActiveContextKey ?? "self", state.ActiveEventContextId);
    }

    private static void Practice(UserLearningState state, string context = "self", string recipe = "event-details",
        LearningPracticeAction action = LearningPracticeAction.Start, LearningPracticeChoice choice = LearningPracticeChoice.None,
        int step = 0, int value = 0, Guid? intent = null, long? expected = null, int version = 1)
    {
        LearningPracticeState? existing = state.Practices.SingleOrDefault(p => p.ContextKey == context && p.RecipeKey == recipe);
        state.ApplyPractice(existing?.Id ?? Id(), context, recipe, version, action, choice, step, value,
            intent ?? Id(), expected ?? state.Revision, existing?.Revision ?? 0, Now);
    }

    [Test]
    public async Task ImmutableOwnershipRejectsInterfaceTenantReassignment()
    {
        UserLearningState state = Learner();
        TenantLearningSpace space = TenantLearningSpace.Create(Id(), state.TenantId, Now);
        await Assert.That(() => ((ITenantEntity)state).TenantId = Id()).Throws<InvalidOperationException>();
        await Assert.That(() => ((ITenantEntity)space).TenantId = Id()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IdentityAndUtcAreRequiredAtCreation()
    {
        await Assert.That(() => UserLearningState.Create(Id(), Id(), Guid.Empty, Now)).Throws<ArgumentException>();
        await Assert.That(() => UserLearningState.Create(Guid.NewGuid(), Id(), Id(), Now)).Throws<ArgumentException>();
        await Assert.That(() => TenantLearningSpace.Create(Id(), Id(), DateTime.SpecifyKind(Now, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TombstonePreservesIdentityUntilExplicitRecreation()
    {
        TenantLearningSpace space = TenantLearningSpace.Create(Id(), Id(), Now);
        Guid id = space.Id;
        space.Remove(0, Now);
        await Assert.That(space.IsRemoved).IsTrue();
        await Assert.That(space.Revision).IsEqualTo(1);
        await Assert.That(space.RemovedAtUtc).IsEqualTo(Now);
        space.Recreate(1, Now.AddSeconds(1));
        await Assert.That(space.Id).IsEqualTo(id);
        await Assert.That(space.IsRemoved).IsFalse();
        await Assert.That(space.Revision).IsEqualTo(2);
        await Assert.That(space.PackKey).IsEqualTo("learning-core");
        await Assert.That(space.PackVersion).IsEqualTo(1);
    }

    [Test]
    public async Task SpaceLifecycleRejectsStaleRevisionAndInvalidTransitions()
    {
        TenantLearningSpace space = TenantLearningSpace.Create(Id(), Id(), Now);
        await Assert.That(() => space.Recreate(0, Now)).Throws<InvalidOperationException>();
        space.Remove(0, Now);
        await Assert.That(() => space.Recreate(0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => space.Remove(1, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PreferencesArePrivateRevisionedAndDoNotRenewClaims()
    {
        UserLearningState state = Learner();
        Start(state);
        DateTime? expiry = state.ClaimExpiresAtUtc;
        state.UpdatePreferences(true, true, false, state.Revision, Now.AddSeconds(90));
        await Assert.That(state.SuppressAutomaticTours).IsTrue();
        await Assert.That(state.IsHidden).IsTrue();
        await Assert.That(state.IsPinned).IsFalse();
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(expiry);
        await Assert.That(() => state.UpdatePreferences(false, false, true, 0, Now.AddSeconds(91)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClaimExpiresAtExactlyOneHundredTwentySeconds()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        foreach (LearningTourStep step in Tour().Steps)
            TourAction(state, claim, LearningTourAction.Progress, step.StepKey, step.StepVersion);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(120));
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete,
            now: Now.AddSeconds(120))).Throws<InvalidOperationException>();
        Start(state, replay: true, now: Now.AddSeconds(120));
        await Assert.That(state.ActiveClaimId == claim).IsFalse();
    }

    [Test]
    public async Task OnlyExplicitProgressRenewsTheExclusiveClaim()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        await Assert.That(() => Start(state, family: LearningTourFamily.Studio, now: Now.AddSeconds(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete,
            now: Now.AddSeconds(100))).Throws<InvalidOperationException>();
        TourAction(state, claim, LearningTourAction.Progress, "navigation", 1, now: Now.AddSeconds(100));
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(220));
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete,
            now: Now.AddSeconds(219))).Throws<InvalidOperationException>();
        TourAction(state, claim, LearningTourAction.Progress, "workspace", 1, now: Now.AddSeconds(219));
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete,
            now: Now.AddSeconds(219))).Throws<InvalidOperationException>();
        TourAction(state, claim, LearningTourAction.Progress, "review", 1, now: Now.AddSeconds(219));
        TourAction(state, claim, LearningTourAction.Complete, now: Now.AddSeconds(219));
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Completed);
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(3);
    }

    [Test]
    public async Task StaleClaimRevisionAndBackwardProgressCannotMutateState()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        await Assert.That(() => TourAction(state, Id(), LearningTourAction.Progress, "workspace", 1))
            .Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Progress, "workspace", 1, expected: 0))
            .Throws<InvalidOperationException>();
        TourAction(state, claim, LearningTourAction.Progress, "workspace", 1);
        // Semantic progress is a set: an earlier applicable step is new evidence, not a backwards cursor.
        TourAction(state, claim, LearningTourAction.Progress, "navigation", 1);
        long revision = state.Revision;
        DateTime? expiry = state.ClaimExpiresAtUtc;
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Progress, "workspace", 1))
            .Throws<InvalidOperationException>();
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(2);
        await Assert.That(state.Tours.Single().Steps.Any(step => step.StepKey == "workspace" && step.StepVersion == 1)).IsTrue();
        await Assert.That(state.Tours.Single().Steps.Any(step => step.StepKey == "navigation" && step.StepVersion == 1)).IsTrue();
        await Assert.That(state.Revision).IsEqualTo(revision);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(expiry);
    }

    [Test]
    public async Task InvitationCanBeSkippedWithoutTakingAClaim()
    {
        UserLearningState state = Learner();
        TourAction(state, null, LearningTourAction.Skip, definition: Tour(LearningTourFamily.Studio));
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Skipped);
        await Assert.That(state.Revision).IsEqualTo(1);
        Start(state, family: LearningTourFamily.Shell);
        await Assert.That(() => TourAction(state, null, LearningTourAction.Skip,
            definition: Tour(LearningTourFamily.NormalAi)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, null, LearningTourAction.Snooze,
            definition: Tour(LearningTourFamily.NormalAi), snoozeDuration: LearningSnoozeDuration.OneHour))
            .Throws<InvalidOperationException>();
        TourAction(state, state.ActiveClaimId, LearningTourAction.Interrupt);
        TourAction(state, null, LearningTourAction.Snooze, definition: Tour(LearningTourFamily.NormalAi),
            snoozeDuration: LearningSnoozeDuration.OneHour);
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.Tours.Single(t => t.Family == LearningTourFamily.NormalAi).SnoozedUntilUtc)
            .IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task ReplayAndInterruptionPreservePriorCompletedOrSkippedOutcome()
    {
        foreach (LearningTourOutcome outcome in new[] { LearningTourOutcome.Completed, LearningTourOutcome.Skipped })
        {
            UserLearningState state = Learner();
            Guid first = Id();
            Start(state, first);
            if (outcome == LearningTourOutcome.Completed)
                foreach (LearningTourStep step in Tour().Steps)
                    TourAction(state, first, LearningTourAction.Progress, step.StepKey, step.StepVersion);
            TourAction(state, first, outcome == LearningTourOutcome.Completed
                ? LearningTourAction.Complete : LearningTourAction.Skip);
            await Assert.That(() => Start(state)).Throws<InvalidOperationException>();
            Guid replay = Id();
            Start(state, replay, replay: true);
            await Assert.That(state.Tours.Single().Outcome).IsEqualTo(outcome);
            TourAction(state, replay, LearningTourAction.Interrupt);
            await Assert.That(state.Tours.Single().Outcome).IsEqualTo(outcome);
            await Assert.That(state.ActiveClaimId).IsNull();
        }
    }

    [Test]
    public async Task FourFamiliesRetainOnlyCurrentAndPreviousMaterialVersions()
    {
        UserLearningState state = Learner();
        foreach (LearningTourFamily family in Enum.GetValues<LearningTourFamily>())
        {
            for (int version = 2; version <= 4; version++)
                TourAction(state, null, LearningTourAction.Skip, definition: Tour(family, version));
        }
        await Assert.That(state.Tours.Count).IsEqualTo(8);
        await Assert.That(state.Tours.All(t => t.MaterialVersion is 3 or 4)).IsTrue();
        await Assert.That(() => TourAction(state, null, LearningTourAction.Skip, definition: Tour(version: 2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExactTerminalRetryIsIdempotentButChangedOutcomeIsRejected()
    {
        UserLearningState state = Learner();
        Guid claim = Id(), intent = Id();
        Start(state, claim);
        foreach (LearningTourStep step in Tour().Steps)
            TourAction(state, claim, LearningTourAction.Progress, step.StepKey, step.StepVersion);
        long expected = state.Revision;
        TourAction(state, claim, LearningTourAction.Complete, intent: intent, expected: expected);
        long final = state.Revision;
        TourAction(state, claim, LearningTourAction.Complete, intent: intent, expected: expected, now: Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(final);
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Skip, intent: intent, expected: expected))
            .Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AllSixRecipesUseCanonicalSelfContextAndBoundedMachineFields()
    {
        UserLearningState state = Learner();
        foreach (string recipe in Recipes)
        {
            Practice(state, recipe: recipe);
            Practice(state, recipe: recipe, action: LearningPracticeAction.Choose,
                choice: LearningPracticeChoice.Primary, step: 1, value: 10);
            Practice(state, recipe: recipe, action: LearningPracticeAction.Confirm);
        }
        await Assert.That(state.Practices.Count).IsEqualTo(6);
        await Assert.That(state.Practices.All(p => p.ContextKey == "self" && p.IsConfirmed)).IsTrue();
        await Assert.That(state.Practices.All(p => p.PayloadBytes is > 0 and <= 1024)).IsTrue();
    }

    [Test]
    public async Task TwentyContextsAndSixRecipesFitBothPayloadCapsAndRejectTwentyFirst()
    {
        UserLearningState state = Learner();
        for (int i = 0; i < 20; i++)
        {
            string context = i == 0 ? "self" : $"actor:{Id():D}";
            foreach (string recipe in Recipes) Practice(state, context, recipe);
        }
        await Assert.That(state.Practices.Count).IsEqualTo(120);
        await Assert.That(state.TotalPracticePayloadBytes <= 131072).IsTrue();
        await Assert.That(() => Practice(state, $"actor:{Id():D}")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OwnerCanClearOrphanContextWithoutPreviewAuthority()
    {
        UserLearningState state = Learner();
        string context = $"actor:{Id():D}";
        Practice(state, context);
        Practice(state, context, action: LearningPracticeAction.Clear);
        await Assert.That(state.Practices.Count).IsEqualTo(0);
        await Assert.That(state.TotalPracticePayloadBytes).IsEqualTo(0);
    }

    [Test]
    public async Task PracticeRejectsUnversionedUnknownOrNoncanonicalInput()
    {
        UserLearningState state = Learner();
        await Assert.That(() => Practice(state, null!)).Throws<ArgumentException>();
        await Assert.That(() => Practice(state, "SELF")).Throws<ArgumentException>();
        await Assert.That(() => Practice(state, $"actor:{Guid.Empty:D}")).Throws<ArgumentException>();
        await Assert.That(() => Practice(state, "self", "arbitrary-prompt")).Throws<ArgumentException>();
        await Assert.That(() => Practice(state, version: 2)).Throws<InvalidOperationException>();
        await Assert.That(() => Practice(state, action: (LearningPracticeAction)99)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Practice(state, choice: (LearningPracticeChoice)99)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Practice(state, value: 10001)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(state.Practices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PracticeStateMachineRequiresChoiceBeforeConfirmationAndResetBeforeRestart()
    {
        UserLearningState state = Learner();
        await Assert.That(() => Practice(state, action: LearningPracticeAction.Confirm)).Throws<InvalidOperationException>();
        Practice(state);
        await Assert.That(() => Practice(state, action: LearningPracticeAction.Confirm)).Throws<InvalidOperationException>();
        Practice(state, action: LearningPracticeAction.Choose, choice: LearningPracticeChoice.Secondary, step: 1);
        Practice(state, action: LearningPracticeAction.Confirm);
        await Assert.That(() => Practice(state, action: LearningPracticeAction.Choose,
            choice: LearningPracticeChoice.Primary, step: 2)).Throws<InvalidOperationException>();
        Practice(state, action: LearningPracticeAction.Reset);
        await Assert.That(state.Practices.Single().IsConfirmed).IsFalse();
        await Assert.That(state.Practices.Single().Choice).IsEqualTo(LearningPracticeChoice.None);
    }

    [Test]
    public async Task ConfirmationPersistsTheAuthoredResultStepWithoutLosingItsChoice()
    {
        UserLearningState state = Learner();
        Practice(state);
        Practice(state, action: LearningPracticeAction.Choose,
            choice: LearningPracticeChoice.Primary, step: 1, value: 12);
        Practice(state, action: LearningPracticeAction.Confirm);
        LearningPracticeState confirmed = state.Practices.Single();
        await Assert.That(confirmed.Step).IsEqualTo(2);
        await Assert.That(confirmed.Choice).IsEqualTo(LearningPracticeChoice.Primary);
        await Assert.That(confirmed.Value).IsEqualTo(12);
        await Assert.That(confirmed.IsConfirmed).IsTrue();
    }

    [Test]
    public async Task ExactPracticeRetryCannotAcceptChangedPayloadOrStaleRecipeRevision()
    {
        UserLearningState state = Learner();
        Guid practice = Id(), intent = Id();
        state.ApplyPractice(practice, "self", Recipes[0], 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 0, intent, 0, 0, Now);
        state.ApplyPractice(practice, "self", Recipes[0], 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 0, intent, 0, 0, Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(1);
        await Assert.That(() => state.ApplyPractice(practice, "self", Recipes[0], 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 1, intent, 0, 0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.ApplyPractice(practice, "self", Recipes[0], 1, LearningPracticeAction.Choose,
            LearningPracticeChoice.Primary, 1, 0, Id(), state.Revision, 0, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PublishedChildrenCannotBeMutatedByConsumers()
    {
        UserLearningState state = Learner();
        Practice(state);
        await Assert.That(() => ((ICollection<LearningPracticeState>)state.Practices).Clear()).Throws<NotSupportedException>();
        TourAction(state, null, LearningTourAction.Skip);
        await Assert.That(() => ((ICollection<LearningTourProgress>)state.Tours).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task ChildrenInheritTenantAndLearnerButNeverProductionContextOwnership()
    {
        UserLearningState state = Learner();
        Guid userId = state.UserId;
        Practice(state, $"actor:{Id():D}");
        TourAction(state, null, LearningTourAction.Skip);
        LearningPracticeState practice = state.Practices.Single();
        LearningTourProgress tour = state.Tours.Single();
        await Assert.That(practice.TenantId).IsEqualTo(state.TenantId);
        await Assert.That(tour.TenantId).IsEqualTo(state.TenantId);
        await Assert.That(practice.UserLearningStateId).IsEqualTo(state.Id);
        await Assert.That(tour.UserLearningStateId).IsEqualTo(state.Id);
        await Assert.That(state.UserId).IsEqualTo(userId);
        await Assert.That(() => ((ITenantEntity)practice).TenantId = Id()).Throws<InvalidOperationException>();
        await Assert.That(() => ((ITenantEntity)tour).TenantId = Id()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClaimAndInvitationRetriesAreExactAndCannotChangeTheirPayload()
    {
        UserLearningState state = Learner();
        Guid progress = Id(), claim = Id(), intent = Id();
        LearningTourDefinition definition = Tour(LearningTourFamily.EventManagement);
        state.ClaimSemanticTour(progress, definition, claim, intent, 0, Now, PlanFingerprint, PolicySourceStamp);
        state.ClaimSemanticTour(progress, definition, claim, intent, 0, Now.AddSeconds(1), PlanFingerprint, PolicySourceStamp);
        await Assert.That(state.Revision).IsEqualTo(1);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(120));
        await Assert.That(state.ActivePlanFingerprint).IsEqualTo(PlanFingerprint);
        await Assert.That(state.ActivePolicySourceStamp).IsEqualTo(PolicySourceStamp);
        await Assert.That(() => state.ClaimSemanticTour(progress, definition, claim,
            intent, 0, Now, PlanFingerprint, PolicySourceStamp, eventContextId: Id())).Throws<InvalidOperationException>();
        await Assert.That(() => state.ClaimSemanticTour(progress, definition, claim,
            intent, 0, Now, new string('c', 64), PolicySourceStamp)).Throws<InvalidOperationException>();
        await Assert.That(() => state.ClaimSemanticTour(progress, definition, claim,
            intent, 0, Now, PlanFingerprint, new string('d', 64))).Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Interrupt, definition: definition,
            planFingerprint: new string('c', 64))).Throws<InvalidOperationException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Interrupt, definition: definition,
            policySourceStamp: new string('d', 64))).Throws<InvalidOperationException>();
        TourAction(state, claim, LearningTourAction.Interrupt, definition: definition);
        Guid skip = Id();
        long expected = state.Revision;
        TourAction(state, null, LearningTourAction.Skip, intent: skip, expected: expected, definition: definition);
        TourAction(state, null, LearningTourAction.Skip, intent: skip, expected: expected,
            definition: definition, now: Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(3);
        await Assert.That(() => TourAction(state, null, LearningTourAction.Skip, intent: skip, expected: expected,
            definition: Tour(LearningTourFamily.EventManagement, 3), progressId: progress))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RepeatedStepAndPracticeActivityCannotKeepATourAlive()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        foreach (LearningTourStep step in Tour().Steps)
            TourAction(state, claim, LearningTourAction.Progress, step.StepKey, step.StepVersion, now: Now.AddSeconds(30));
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Progress, "navigation", 1,
            now: Now.AddSeconds(100))).Throws<InvalidOperationException>();
        PracticeAt(state, Now.AddSeconds(120));
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(150));
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete,
            now: Now.AddSeconds(150))).Throws<InvalidOperationException>();
    }

    private static void PracticeAt(UserLearningState state, DateTime utcNow) =>
        state.ApplyPractice(Id(), "self", Recipes[0], 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 0, Id(), state.Revision, 0, utcNow);

    [Test]
    public async Task ExpiredClaimReplacementRejectsLateOutcomeAndBindsTemporaryContext()
    {
        UserLearningState state = Learner();
        Guid oldClaim = Id();
        Start(state, oldClaim);
        Guid current = Id(), eventId = Id();
        string context = $"actor:{Id():D}";
        LearningTourDefinition definition = Tour(LearningTourFamily.EventManagement);
        state.ClaimSemanticTour(Id(), definition, current, Id(), state.Revision,
            Now.AddSeconds(120), PlanFingerprint, PolicySourceStamp, context, eventId);
        foreach (LearningTourStep step in definition.Steps)
            TourAction(state, current, LearningTourAction.Progress, step.StepKey, step.StepVersion,
                definition: definition, now: Now.AddSeconds(120));
        await Assert.That(state.ActiveContextKey).IsEqualTo(context);
        await Assert.That(state.ActiveEventContextId).IsEqualTo(eventId);
        await Assert.That(() => TourAction(state, oldClaim, LearningTourAction.Complete,
            definition: definition, now: Now.AddSeconds(121))).Throws<InvalidOperationException>();
        TourAction(state, current, LearningTourAction.Skip, definition: definition, now: Now.AddSeconds(121));
        await Assert.That(state.ActiveContextKey).IsNull();
        await Assert.That(state.ActiveEventContextId).IsNull();
    }

    [Test]
    public async Task ClearIsExactIdempotentAndReleasesTheLastOrphanContextSlot()
    {
        UserLearningState state = Learner();
        string orphan = $"actor:{Id():D}";
        Practice(state, orphan);
        for (int i = 1; i < 20; i++) Practice(state, $"actor:{Id():D}");
        LearningPracticeState practice = state.Practices.Single(p => p.ContextKey == orphan);
        Guid intent = Id();
        long expected = state.Revision;
        state.ApplyPractice(practice.Id, orphan, Recipes[0], 1, LearningPracticeAction.Clear,
            LearningPracticeChoice.None, 0, 0, intent, expected, practice.Revision, Now);
        state.ApplyPractice(practice.Id, orphan, Recipes[0], 1, LearningPracticeAction.Clear,
            LearningPracticeChoice.None, 0, 0, intent, expected, practice.Revision, Now.AddSeconds(1));
        await Assert.That(state.Practices.Count).IsEqualTo(19);
        await Assert.That(() => state.ApplyPractice(practice.Id, orphan, Recipes[1], 1, LearningPracticeAction.Clear,
            LearningPracticeChoice.None, 0, 0, intent, expected, practice.Revision, Now))
            .Throws<InvalidOperationException>();
        Practice(state, $"actor:{Id():D}");
        await Assert.That(state.Practices.Count).IsEqualTo(20);
    }

    [Test]
    public async Task RejectedOperationsLeaveRevisionCollectionsAndClaimUnchanged()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        long revision = state.Revision;
        DateTime? expiry = state.ClaimExpiresAtUtc;
        await Assert.That(() => state.ClaimSemanticTour(Id(), Tour((LearningTourFamily)99),
            Id(), Id(), revision, Now, PlanFingerprint, PolicySourceStamp))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TourAction(state, claim, (LearningTourAction)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TourAction(state, claim, (LearningTourAction)0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Progress, "navigation", 33))
            .Throws<ArgumentException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Complete, "navigation", 1))
            .Throws<ArgumentException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Progress, "not-applicable", 1))
            .Throws<ArgumentException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Interrupt, planFingerprint: new string('a', 63)))
            .Throws<ArgumentException>();
        await Assert.That(() => TourAction(state, claim, LearningTourAction.Interrupt, policySourceStamp: new string('g', 64)))
            .Throws<ArgumentException>();
        await Assert.That(() => state.UpdatePreferences(false, false, false, revision, Now.AddTicks(-1)))
            .Throws<InvalidOperationException>();
        await Assert.That(state.Revision).IsEqualTo(revision);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(expiry);
        await Assert.That(state.Tours.Count).IsEqualTo(1);
        await Assert.That(state.Practices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PracticeDoesNotAcceptMismatchedIdentityOrIgnoredArbitraryChoiceFields()
    {
        UserLearningState state = Learner();
        Practice(state);
        long expected = state.Revision;
        LearningPracticeState practice = state.Practices.Single();
        await Assert.That(() => state.ApplyPractice(Id(), "self", Recipes[0], 1, LearningPracticeAction.Choose,
            LearningPracticeChoice.Primary, 1, 0, Id(), expected, practice.Revision, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => state.ApplyPractice(practice.Id, "self", Recipes[1], 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 0, Id(), expected, 0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => Practice(state, action: LearningPracticeAction.Reset, value: 1)).Throws<ArgumentException>();
        await Assert.That(() => Practice(state, action: LearningPracticeAction.Choose,
            choice: LearningPracticeChoice.None)).Throws<InvalidOperationException>();
        await Assert.That(state.Revision).IsEqualTo(expected);
        await Assert.That(practice.Revision).IsEqualTo(1);
    }
}
