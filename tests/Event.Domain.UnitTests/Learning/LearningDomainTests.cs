using Explore.Domain.Learning;

namespace Event.Domain.UnitTests.Learning;

public sealed class LearningDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Recipes =
        ["event-details", "sessions-program", "ticket-capacity", "attendees-team", "publication-lifecycle", "ai-example-review"];

    private static Guid Id() => Guid.CreateVersion7();
    private static UserLearningState Learner() => UserLearningState.Create(Id(), Id(), Id(), Now);
    private static void Start(UserLearningState state, Guid? claim = null, int version = 1,
        LearningTourFamily family = LearningTourFamily.Shell, bool replay = false, DateTime? now = null) =>
        state.ClaimTour(state.Tours.SingleOrDefault(t => t.Family == family && t.MaterialVersion == version)?.Id ?? Id(),
            family, version, claim ?? Id(), Id(), state.Revision, now ?? Now, replay: replay);

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
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(120));
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Completed, 3,
            state.Revision, Now.AddSeconds(120))).Throws<InvalidOperationException>();
        Start(state, now: Now.AddSeconds(120));
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
        state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 1, state.Revision, Now.AddSeconds(100));
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(220));
        state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Completed, 3, state.Revision, Now.AddSeconds(219));
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Completed);
    }

    [Test]
    public async Task StaleClaimRevisionAndBackwardProgressCannotMutateState()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        await Assert.That(() => state.RecordTourOutcome(Id(), Id(), LearningTourOutcome.Completed, 3,
            state.Revision, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Completed, 3, 0, Now))
            .Throws<InvalidOperationException>();
        state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 2, state.Revision, Now);
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 1,
            state.Revision, Now)).Throws<InvalidOperationException>();
        await Assert.That(state.Tours.Single().Step).IsEqualTo(2);
    }

    [Test]
    public async Task InvitationCanBeSkippedWithoutTakingAClaim()
    {
        UserLearningState state = Learner();
        state.SkipInvitation(Id(), LearningTourFamily.Studio, 1, Id(), 0, Now);
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Skipped);
        await Assert.That(state.Revision).IsEqualTo(1);
        Start(state, family: LearningTourFamily.Shell);
        await Assert.That(() => state.SkipInvitation(Id(), LearningTourFamily.NormalAi, 1, Id(), state.Revision, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReplayAndInterruptionPreservePriorCompletedOrSkippedOutcome()
    {
        foreach (LearningTourOutcome outcome in new[] { LearningTourOutcome.Completed, LearningTourOutcome.Skipped })
        {
            UserLearningState state = Learner();
            Guid first = Id();
            Start(state, first);
            state.RecordTourOutcome(first, Id(), outcome, 3, state.Revision, Now);
            await Assert.That(() => Start(state)).Throws<InvalidOperationException>();
            Guid replay = Id();
            Start(state, replay, replay: true);
            await Assert.That(state.Tours.Single().Outcome).IsEqualTo(outcome);
            state.RecordTourOutcome(replay, Id(), LearningTourOutcome.Interrupted, 0, state.Revision, Now);
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
            for (int version = 1; version <= 3; version++)
                state.SkipInvitation(Id(), family, version, Id(), state.Revision, Now);
        }
        await Assert.That(state.Tours.Count).IsEqualTo(8);
        await Assert.That(state.Tours.All(t => t.MaterialVersion is 2 or 3)).IsTrue();
        await Assert.That(() => state.SkipInvitation(Id(), LearningTourFamily.Shell, 1, Id(), state.Revision, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExactTerminalRetryIsIdempotentButChangedOutcomeIsRejected()
    {
        UserLearningState state = Learner();
        Guid claim = Id(), intent = Id();
        Start(state, claim);
        long expected = state.Revision;
        state.RecordTourOutcome(claim, intent, LearningTourOutcome.Completed, 3, expected, Now);
        long final = state.Revision;
        state.RecordTourOutcome(claim, intent, LearningTourOutcome.Completed, 3, expected, Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(final);
        await Assert.That(() => state.RecordTourOutcome(claim, intent, LearningTourOutcome.Skipped, 3, expected, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Completed, 3,
            state.Revision, Now)).Throws<InvalidOperationException>();
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
        state.SkipInvitation(Id(), LearningTourFamily.Shell, 1, Id(), state.Revision, Now);
        await Assert.That(() => ((ICollection<LearningTourProgress>)state.Tours).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task ChildrenInheritTenantAndLearnerButNeverProductionContextOwnership()
    {
        UserLearningState state = Learner();
        Guid userId = state.UserId;
        Practice(state, $"actor:{Id():D}");
        state.SkipInvitation(Id(), LearningTourFamily.Shell, 1, Id(), state.Revision, Now);
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
        state.ClaimTour(progress, LearningTourFamily.EventManagement, 1, claim, intent, 0, Now);
        state.ClaimTour(progress, LearningTourFamily.EventManagement, 1, claim, intent, 0, Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(1);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(120));
        await Assert.That(() => state.ClaimTour(progress, LearningTourFamily.EventManagement, 1, claim,
            intent, 0, Now, eventContextId: Id())).Throws<InvalidOperationException>();
        state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Interrupted, 0, state.Revision, Now);
        Guid skip = Id();
        long expected = state.Revision;
        state.SkipInvitation(progress, LearningTourFamily.EventManagement, 1, skip, expected, Now);
        state.SkipInvitation(progress, LearningTourFamily.EventManagement, 1, skip, expected, Now.AddSeconds(1));
        await Assert.That(state.Revision).IsEqualTo(3);
        await Assert.That(() => state.SkipInvitation(progress, LearningTourFamily.EventManagement, 2,
            skip, expected, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RepeatedStepAndPracticeActivityCannotKeepATourAlive()
    {
        UserLearningState state = Learner();
        Guid claim = Id();
        Start(state, claim);
        state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 1, state.Revision, Now.AddSeconds(30));
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 1,
            state.Revision, Now.AddSeconds(100))).Throws<InvalidOperationException>();
        PracticeAt(state, Now.AddSeconds(120));
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(150));
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.Completed, 3,
            state.Revision, Now.AddSeconds(150))).Throws<InvalidOperationException>();
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
        state.ClaimTour(Id(), LearningTourFamily.EventManagement, 1, current, Id(), state.Revision,
            Now.AddSeconds(120), context, eventId);
        await Assert.That(state.ActiveContextKey).IsEqualTo(context);
        await Assert.That(state.ActiveEventContextId).IsEqualTo(eventId);
        await Assert.That(() => state.RecordTourOutcome(oldClaim, Id(), LearningTourOutcome.Completed, 3,
            state.Revision, Now.AddSeconds(121))).Throws<InvalidOperationException>();
        state.RecordTourOutcome(current, Id(), LearningTourOutcome.Skipped, 0, state.Revision, Now.AddSeconds(121));
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
        await Assert.That(() => state.ClaimTour(Id(), (LearningTourFamily)99, 1, Id(), Id(), revision, Now))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), (LearningTourOutcome)99, 0, revision, Now))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.None, 0, revision, Now))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => state.RecordTourOutcome(claim, Id(), LearningTourOutcome.InProgress, 33, revision, Now))
            .Throws<ArgumentOutOfRangeException>();
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
