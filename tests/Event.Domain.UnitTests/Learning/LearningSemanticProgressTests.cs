using System.Globalization;
using Explore.Domain.Learning;

namespace Event.Domain.UnitTests.Learning;

public sealed class LearningSemanticProgressTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Fingerprint = new('A', 64);
    private static readonly string PolicyStamp = new('B', 64);

    private static UserLearningState Learner() =>
        UserLearningState.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static LearningTourDefinition Definition(int materialVersion = 2,
        params LearningTourStep[] steps) =>
        new(LearningTourFamily.Shell, materialVersion,
            steps.Length == 0 ? [new("shell.scope", 1), new("shell.help", 1)] : steps);

    private static Guid Start(UserLearningState state, LearningTourDefinition definition,
        DateTime? now = null, bool replay = false)
    {
        Guid claim = Guid.CreateVersion7();
        state.ClaimSemanticTour(state.Tours.SingleOrDefault(tour => tour.Family == definition.Family
                && tour.MaterialVersion == definition.MaterialVersion)?.Id ?? Guid.CreateVersion7(),
            definition, claim, Guid.CreateVersion7(),
            state.Revision, now ?? Now, Fingerprint, PolicyStamp, replay: replay);
        return claim;
    }

    private static void Act(UserLearningState state, LearningTourDefinition definition,
        Guid? claim, LearningTourAction action, string? key = null, int? version = null,
        DateTime? now = null) =>
        state.RecordSemanticTourAction(state.Tours.SingleOrDefault(tour => tour.Family == definition.Family
                && tour.MaterialVersion == definition.MaterialVersion)?.Id ?? Guid.CreateVersion7(), definition, claim,
            Guid.CreateVersion7(), action, key, version, null, Fingerprint, PolicyStamp,
            state.Revision, now ?? Now);

    [Test]
    public async Task ReorderingAndOmissionPreserveSemanticCoverageWithoutWritingOnRead()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition();
        Guid claim = Start(state, definition);
        Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 1);
        long revision = state.Revision;

        await Assert.That(state.HasTourCoverage(Definition(2, new LearningTourStep("shell.scope", 1)))).IsTrue();
        await Assert.That(state.HasTourCoverage(Definition(2, new("shell.help", 1), new("shell.scope", 1)))).IsFalse();
        await Assert.That(state.Revision).IsEqualTo(revision);
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitProgressPreservesOmittedButStillAuthoredCompletion()
    {
        UserLearningState state = Learner();
        LearningTourDefinition full = Definition();
        Guid first = Start(state, full);
        Act(state, full, first, LearningTourAction.Progress, "shell.scope", 1);
        Act(state, full, first, LearningTourAction.Interrupt);
        var partial = new LearningTourDefinition(full.Family, full.MaterialVersion,
            [new("shell.help", 1)], full.Steps);
        Guid next = Start(state, partial);
        Act(state, partial, next, LearningTourAction.Progress, "shell.help", 1);
        Act(state, partial, next, LearningTourAction.Complete);

        await Assert.That(state.HasTourCoverage(full)).IsTrue();
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CompletionRequiresEveryCurrentlyApplicableSemanticStep()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition();
        Guid claim = Start(state, definition);
        Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 1);
        await Assert.That(() => Act(state, definition, claim, LearningTourAction.Complete))
            .Throws<InvalidOperationException>();
        Act(state, definition, claim, LearningTourAction.Progress, "shell.help", 1);
        Act(state, definition, claim, LearningTourAction.Complete);

        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Completed);
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.HasTourCoverage(definition)).IsTrue();
        await Assert.That(state.HasTourCoverage(Definition(2, new("shell.scope", 2), new("shell.help", 1)))).IsFalse();
        await Assert.That(state.HasTourCoverage(Definition(2, new("shell.scope", 1), new("shell.help", 1),
            new("shell.studio", 1)))).IsFalse();
    }

    [Test]
    public async Task UnknownOrChangedStepCannotRenewClaimOrAddEvidence()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition();
        Guid claim = Start(state, definition);
        DateTime? expiry = state.ClaimExpiresAtUtc;
        await Assert.That(() => Act(state, definition, claim, LearningTourAction.Progress, "shell.unknown", 1))
            .Throws<ArgumentException>();
        await Assert.That(() => Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 2))
            .Throws<ArgumentException>();
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(expiry);
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitProgressAloneRenewsClaimAndExpiryIsExclusive()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition();
        Guid claim = Start(state, definition);
        Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 1, Now.AddSeconds(100));
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(220));
        await Assert.That(() => Act(state, definition, claim, LearningTourAction.Progress,
            "shell.scope", 1, Now.AddSeconds(110))).Throws<InvalidOperationException>();
        await Assert.That(() => Act(state, definition, claim, LearningTourAction.Progress,
            "shell.help", 1, Now.AddSeconds(220))).Throws<InvalidOperationException>();
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(220));
    }

    [Test]
    public async Task ChangedPlanOrPolicyCannotSettleAnExistingClaim()
    {
        foreach (bool changedPolicy in new[] { false, true })
        {
            UserLearningState state = Learner();
            LearningTourDefinition definition = Definition();
            Guid claim = Start(state, definition);
            long revision = state.Revision;
            DateTime? expiry = state.ClaimExpiresAtUtc;
            await Assert.That(() => state.RecordSemanticTourAction(state.Tours.Single().Id, definition,
                claim, Guid.CreateVersion7(), LearningTourAction.Progress, "shell.scope", 1, null,
                changedPolicy ? Fingerprint : new string('C', 64),
                changedPolicy ? new string('C', 64) : PolicyStamp, state.Revision, Now))
                .Throws<InvalidOperationException>();
            await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(0);
            await Assert.That(state.Revision).IsEqualTo(revision);
            await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(expiry);
        }
    }

    [Test]
    public async Task CurrentCoverageUsesCompatibleRetainedMaterialWithoutMigratingOnRead()
    {
        UserLearningState state = Learner();
        LearningTourDefinition previous = Definition(2, new LearningTourStep("shell.scope", 1));
        Guid claim = Start(state, previous);
        Act(state, previous, claim, LearningTourAction.Progress, "shell.scope", 1);
        Act(state, previous, claim, LearningTourAction.Complete);
        long revision = state.Revision;

        await Assert.That(state.HasTourCoverage(Definition(3, new LearningTourStep("shell.scope", 1)))).IsTrue();
        await Assert.That(state.HasTourCoverage(Definition(3, new LearningTourStep("shell.scope", 2)))).IsFalse();
        await Assert.That(state.Tours.Single().MaterialVersion).IsEqualTo(2);
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Completed);
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(1);
        await Assert.That(state.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task SkipAndSnoozeDoNotInventCompletionOrRequireStartingAnOverlay()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition();
        state.RecordSemanticTourAction(Guid.CreateVersion7(), definition, null, Guid.CreateVersion7(),
            LearningTourAction.Snooze, null, null, LearningSnoozeDuration.OneDay,
            Fingerprint, PolicyStamp, 0, Now);
        await Assert.That(state.Tours.Single().SnoozedUntilUtc).IsEqualTo(Now.AddDays(1));
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.HasTourCoverage(definition)).IsFalse();
        Act(state, definition, null, LearningTourAction.Skip);
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Skipped);
        await Assert.That(state.HasTourCoverage(definition)).IsFalse();
    }

    [Test]
    public async Task MaterialCarryForwardOccursBeforePruningAndDoesNotCopyIncompatibleEvidence()
    {
        UserLearningState state = Learner();
        LearningTourDefinition first = Definition();
        Guid claim = Start(state, first);
        Act(state, first, claim, LearningTourAction.Progress, "shell.scope", 1);
        Act(state, first, claim, LearningTourAction.Interrupt);
        LearningTourDefinition second = Definition(3, new("shell.scope", 1), new("shell.help", 2));
        Guid next = Start(state, second);
        await Assert.That(state.Tours.Single(t => t.MaterialVersion == 3).Steps.Single().StepKey)
            .IsEqualTo("shell.scope");
        Act(state, second, next, LearningTourAction.Interrupt);
        Start(state, Definition(4, new LearningTourStep("shell.scope", 1)), replay: true);
        await Assert.That(state.Tours.Count).IsEqualTo(2);
        await Assert.That(state.Tours.Any(t => t.MaterialVersion == 2)).IsFalse();
        await Assert.That(state.HasTourCoverage(Definition(4, new LearningTourStep("shell.scope", 1)))).IsTrue();
    }

    [Test]
    public async Task ReplayInterruptionPreservesHistoricalCompletion()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition(2, new LearningTourStep("shell.scope", 1));
        Guid claim = Start(state, definition);
        Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 1);
        Act(state, definition, claim, LearningTourAction.Complete);
        Guid replay = Start(state, definition, replay: true);
        Act(state, definition, replay, LearningTourAction.Interrupt);
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.Completed);
        await Assert.That(state.HasTourCoverage(definition)).IsTrue();
    }

    [Test]
    public async Task SemanticActionBindsTemporaryContextAndExactReleasedRetry()
    {
        UserLearningState state = Learner();
        var definition = new LearningTourDefinition(LearningTourFamily.EventManagement, 2,
            [new("event.scope", 1)]);
        Guid progress = Guid.CreateVersion7(), claim = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        string context = $"actor:{Guid.CreateVersion7():D}";
        state.ClaimSemanticTour(progress, definition, claim, Guid.CreateVersion7(), 0, Now,
            Fingerprint, PolicyStamp, context, eventId);
        await Assert.That(() => state.RecordSemanticTourAction(progress, definition, claim,
            Guid.CreateVersion7(), LearningTourAction.Interrupt, null, null, null,
            Fingerprint, PolicyStamp, state.Revision, Now, "self"))
            .Throws<InvalidOperationException>();
        Guid intent = Guid.CreateVersion7();
        long expected = state.Revision;
        state.RecordSemanticTourAction(progress, definition, claim, intent,
            LearningTourAction.Interrupt, null, null, null, Fingerprint, PolicyStamp,
            expected, Now, context, eventId);
        long settled = state.Revision;
        state.RecordSemanticTourAction(progress, definition, claim, intent,
            LearningTourAction.Interrupt, null, null, null, Fingerprint, PolicyStamp,
            expected, Now.AddSeconds(1), context, eventId);
        await Assert.That(state.Revision).IsEqualTo(settled);
        await Assert.That(state.ActiveContextKey).IsNull();
        await Assert.That(() => state.RecordSemanticTourAction(progress, definition, claim, intent,
            LearningTourAction.Interrupt, null, null, null, Fingerprint, PolicyStamp,
            expected, Now.AddSeconds(1), "self")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DefinitionSnapshotsStepsAndRejectsDuplicateOrUnboundedSemanticIdentities()
    {
        LearningTourStep[] steps = [new("shell.scope", 1)];
        LearningTourDefinition definition = new(LearningTourFamily.Shell, 2, steps);
        steps[0] = new("shell.help", 1);
        await Assert.That(definition.Steps[0].StepKey).IsEqualTo("shell.scope");
        await Assert.That(() => Definition(2, new("shell.scope", 1), new("shell.scope", 2)))
            .Throws<ArgumentException>();
        await Assert.That(() => Definition(2, Enumerable.Range(0, 33)
            .Select(i => new LearningTourStep($"shell.step{i}", 1)).ToArray()))
            .Throws<ArgumentException>();
        await Assert.That(() => new LearningTourStep(new string('a', 97), 1)).Throws<ArgumentException>();
        await Assert.That(() => new LearningTourStep("shell.scope", 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task FourFamiliesAndTwoVersionsBoundEvidenceToTwoHundredFiftySixRows()
    {
        UserLearningState state = Learner();
        foreach (LearningTourFamily family in Enum.GetValues<LearningTourFamily>())
        {
            for (int version = 2; version <= 3; version++)
            {
                var definition = new LearningTourDefinition(family, version, Enumerable.Range(0, 32)
                    .Select(index => new LearningTourStep(
                        $"step.{index.ToString(CultureInfo.InvariantCulture)}", version)).ToArray());
                Guid claim = Start(state, definition);
                foreach (LearningTourStep step in definition.Steps)
                    Act(state, definition, claim, LearningTourAction.Progress, step.StepKey, step.StepVersion);
                Act(state, definition, claim, LearningTourAction.Complete);
            }
        }

        await Assert.That(state.Tours.Count).IsEqualTo(8);
        await Assert.That(state.Tours.Sum(tour => tour.Steps.Count)).IsEqualTo(256);
        foreach (LearningTourFamily family in Enum.GetValues<LearningTourFamily>())
            await Assert.That(state.Tours.Count(tour => tour.Family == family)).IsEqualTo(2);
    }

    [Test]
    public async Task ExactSemanticSettlementRetryDoesNotReopenAnExpiredClaimOrAdvanceRevision()
    {
        UserLearningState state = Learner();
        LearningTourDefinition definition = Definition(2, new LearningTourStep("shell.scope", 1));
        Guid claim = Start(state, definition);
        Act(state, definition, claim, LearningTourAction.Progress, "shell.scope", 1);
        Guid progressId = state.Tours.Single().Id;
        Guid intentId = Guid.CreateVersion7();
        long expected = state.Revision;
        state.RecordSemanticTourAction(progressId, definition, claim, intentId, LearningTourAction.Complete,
            null, null, null, Fingerprint, PolicyStamp, expected, Now);
        long settled = state.Revision;

        state.RecordSemanticTourAction(progressId, definition, claim, intentId, LearningTourAction.Complete,
            null, null, null, Fingerprint, PolicyStamp, expected, Now.AddMinutes(3));

        await Assert.That(state.Revision).IsEqualTo(settled);
        await Assert.That(state.ActiveClaimId).IsNull();
        await Assert.That(state.ActivePlanFingerprint).IsNull();
        await Assert.That(state.ActivePolicySourceStamp).IsNull();
        await Assert.That(() => state.RecordSemanticTourAction(progressId, definition, claim, intentId,
            LearningTourAction.Skip, null, null, null, Fingerprint, PolicyStamp, expected, Now.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }
}
