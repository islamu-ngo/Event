using Explore.Domain.Learning;

namespace Event.Domain.UnitTests.Learning;

public sealed class LearningRequirementAcknowledgementTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string Stamp = new('A', 64);
    private static Guid Id() => Guid.CreateVersion7();
    private static UserLearningState Learner() => UserLearningState.Create(Id(), Id(), Id(), Now);
    private static LearningRequirementDefinition Material(int version) =>
        new("tenant-admin-orientation", version);

    [Test]
    public async Task ExactRetryRetainsIdentityAndRejectsChangedIntentPayload()
    {
        var state = Learner();
        Guid child = Id(), intent = Id();
        var recorded = state.AcknowledgeRequirement(child, LearningRequirementDefinition.Current,
            Stamp, intent, 0, Now);
        state.UpdatePreferences(true, true, false, state.Revision, Now);
        var replay = state.AcknowledgeRequirement(child, LearningRequirementDefinition.Current,
            Stamp, intent, 0, Now.AddSeconds(1));
        await Assert.That(replay.Id).IsEqualTo(recorded.Id);
        await Assert.That(replay.AcknowledgedAtUtc).IsEqualTo(Now);
        await Assert.That(state.Revision).IsEqualTo(2);
        await Assert.That(() => state.AcknowledgeRequirement(child, Material(1),
            new string('B', 64), intent, 0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, intent, 0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(child, Material(2),
            Stamp, intent, 0, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(child, Material(1),
            Stamp, intent, 2, Now)).Throws<InvalidOperationException>();
        await Assert.That(state.RequirementAcknowledgements.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PolicyChangeRequiresExplicitAcknowledgementAndRetainsMaterialIdentity()
    {
        var state = Learner();
        Guid child = Id();
        var original = state.AcknowledgeRequirement(child, Material(1), Stamp, Id(), 0, Now);
        string changed = new('B', 64);
        await Assert.That(original.PolicySourceStamp).IsEqualTo(Stamp);
        var renewed = state.AcknowledgeRequirement(child, Material(1), changed, Id(), 1, Now.AddSeconds(1));
        await Assert.That(renewed.Id).IsEqualTo(child);
        await Assert.That(renewed.PolicySourceStamp).IsEqualTo(changed);
        await Assert.That(renewed.AcknowledgedAtUtc).IsEqualTo(Now.AddSeconds(1));
        var next = state.AcknowledgeRequirement(Id(), Material(2), changed, Id(), 2, Now.AddSeconds(2));
        await Assert.That(next.Id == child).IsFalse();
        await Assert.That(next.MaterialVersion).IsEqualTo(2);
        await Assert.That(state.RequirementAcknowledgements.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AuthoredMaterialTransitionsRetainOnlyCurrentAndPreviousEvidence()
    {
        var state = Learner();
        Guid first = Id();
        state.AcknowledgeRequirement(first, Material(1), Stamp, Id(), 0, Now);
        state.AcknowledgeRequirement(Id(), Material(2), Stamp, Id(), 1, Now);
        state.AcknowledgeRequirement(Id(), Material(3), Stamp, Id(), 2, Now);
        await Assert.That(state.RequirementAcknowledgements.Select(row => row.MaterialVersion))
            .IsEquivalentTo([2, 3]);
        await Assert.That(() => state.AcknowledgeRequirement(first, Material(1),
            Stamp, Id(), 3, Now)).Throws<InvalidOperationException>();
        state.AcknowledgeRequirement(Id(), Material(5), Stamp, Id(), 3, Now);
        await Assert.That(state.RequirementAcknowledgements.Select(row => row.MaterialVersion))
            .IsEquivalentTo([5]);
    }

    [Test]
    public async Task InvalidDefinitionStampTimeRevisionAndIdentityLeaveEvidenceUnchanged()
    {
        var state = Learner();
        await Assert.That(() => new LearningRequirementDefinition("uploaded-key", 1)).Throws<ArgumentException>();
        await Assert.That(() => Material(0)).Throws<ArgumentOutOfRangeException>();
        foreach (string invalid in new[] { "", new string('x', 64), new string('A', 65) })
            await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
                invalid, Id(), 0, Now)).Throws<ArgumentException>();
        await Assert.That(() => state.AcknowledgeRequirement(Guid.Empty, Material(1),
            Stamp, Id(), 0, Now)).Throws<ArgumentException>();
        await Assert.That(() => state.AcknowledgeRequirement(Guid.NewGuid(), Material(1),
            Stamp, Id(), 0, Now)).Throws<ArgumentException>();
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, Guid.Empty, 0, Now)).Throws<ArgumentException>();
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, Id(), 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, Id(), 0, Now.AddTicks(-1))).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, Id(), 0, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified))).Throws<ArgumentException>();
        await Assert.That(state.Revision).IsEqualTo(0);
        await Assert.That(state.RequirementAcknowledgements.Count).IsEqualTo(0);
        Guid child = Id();
        state.AcknowledgeRequirement(child, Material(1), Stamp, Id(), 0, Now);
        await Assert.That(() => state.AcknowledgeRequirement(Id(), Material(1),
            Stamp, Id(), 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => state.AcknowledgeRequirement(child, Material(2),
            Stamp, Id(), 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(state.RequirementAcknowledgements.Single().Id).IsEqualTo(child);
        await Assert.That(state.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task AcknowledgementDoesNotChangePreferencesTourPracticeOrClaim()
    {
        var state = Learner();
        var tour = new LearningTourDefinition(LearningTourFamily.Shell, 2, [new("navigation", 1)]);
        Guid claim = Id();
        state.ClaimSemanticTour(Id(), tour, claim, Id(), 0, Now, Stamp, Stamp);
        state.ApplyPractice(Id(), "self", "event-details", 1, LearningPracticeAction.Start,
            LearningPracticeChoice.None, 0, 0, Id(), 1, 0, Now);
        state.UpdatePreferences(true, true, true, 2, Now);
        var practice = state.Practices.Single();
        long practiceRevision = practice.Revision;
        var acknowledgement = state.AcknowledgeRequirement(Id(), Material(1), Stamp, Id(), 3, Now);
        await Assert.That(acknowledgement.TenantId).IsEqualTo(state.TenantId);
        await Assert.That(acknowledgement.UserLearningStateId).IsEqualTo(state.Id);
        await Assert.That(acknowledgement.RequirementKey).IsEqualTo("tenant-admin-orientation");
        await Assert.That(state.SuppressAutomaticTours).IsTrue();
        await Assert.That(state.IsHidden).IsTrue();
        await Assert.That(state.IsPinned).IsTrue();
        await Assert.That(state.ActiveClaimId).IsEqualTo(claim);
        await Assert.That(state.ClaimExpiresAtUtc).IsEqualTo(Now.AddSeconds(120));
        await Assert.That(state.Tours.Single().Outcome).IsEqualTo(LearningTourOutcome.InProgress);
        await Assert.That(state.Tours.Single().Steps.Count).IsEqualTo(0);
        await Assert.That(state.Practices.Single().Id).IsEqualTo(practice.Id);
        await Assert.That(practice.Revision).IsEqualTo(practiceRevision);
    }
}
