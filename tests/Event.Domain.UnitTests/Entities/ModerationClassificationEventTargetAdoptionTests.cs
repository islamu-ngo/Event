namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationClassificationEventTargetAdoptionTests
{
    [Test]
    public async Task AdoptionRetainsExactOrdinalGenerationAndImmutableBirthAfterExpiryRevocation()
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        var human = Guid.NewGuid();
        var adoption = Adopt(state, selection, human);
        var revoker = Guid.NewGuid();
        adoption.Revoke(1, revoker, 13, "Accountable ending after native deletion.", ClassificationControlState.Now.AddDays(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.BindingOrdinal).IsEqualTo(0);
        await Assert.That(adoption.GenerationId).IsEqualTo(state.Generation.Id);
        await Assert.That(adoption.GenerationWriterGeneration).IsEqualTo(3L);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(human);
        await Assert.That(adoption.WriterGeneration).IsEqualTo(12L);
        await Assert.That(adoption.RevocationWriterGeneration).IsEqualTo(13L);
        await Assert.That(adoption.MatchesCurrent(selection, state.Source, state.Review, state.Generation,
            ClassificationControlState.Now.AddSeconds(4))).IsFalse();
        await Assert.That(() => adoption.Revoke(2, revoker, 14, "Repeated ending is forbidden.",
            ClassificationControlState.Now.AddDays(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AdoptionRejectsAllConflictingHumans(int actor)
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        var human = actor switch
        {
            0 => state.Source.OwnerHumanId, 1 => state.Source.ReviewerHumanId,
            2 => state.Source.OwnerControllerHumanId, 3 => state.Source.ReviewerControllerHumanId,
            _ => selection.AuthoredByHumanId
        };
        await Assert.That(() => Adopt(state, selection, human)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ContentUuidCoincidingWithIndependentHumanIsNotHumanSelfApproval()
    {
        var state = new ClassificationControlState();
        var adoption = Adopt(state, state.Select(), state.Generation.EventId);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(state.Generation.EventId);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AdoptionRejectsWrongOrdinalGenerationLifetimeTimeAndWriter(int change)
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        await Assert.That(() => ModerationClassificationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            change == 0 ? 63 : 0, change == 1 ? state.Reobserve() : state.Generation,
            Guid.NewGuid(), change == 4 ? 11 : 12,
            change == 3 ? ClassificationControlState.Now : ClassificationControlState.Now.AddSeconds(2),
            change == 2 ? selection.ValidUntil.AddTicks(1) : ClassificationControlState.Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task RevocationRejectsMissingAccountabilityRegressingTimeAndStaleRevision(int change)
    {
        var state = new ClassificationControlState();
        var adoption = Adopt(state, state.Select(), Guid.NewGuid());
        if (change == 3)
            await Assert.That(() => adoption.Revoke(2, Guid.NewGuid(), 13, "Accountable terminal ending.",
                ClassificationControlState.Now.AddSeconds(3))).Throws<InvalidOperationException>();
        else
            await Assert.That(() => adoption.Revoke(1, change == 0 ? Guid.Empty : Guid.NewGuid(),
                change == 1 ? 12 : 13, "Accountable terminal ending.",
                change == 2 ? ClassificationControlState.Now : ClassificationControlState.Now.AddSeconds(3)))
                .Throws<ArgumentException>();
        await Assert.That(adoption.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task ReviewWithdrawalAndValidityBoundaryCloseCurrentAdoption()
    {
        var state = new ClassificationControlState();
        var selection = state.Select();
        var adoption = Adopt(state, selection, Guid.NewGuid());
        await Assert.That(adoption.MatchesCurrent(selection, state.Source, state.Review, state.Generation,
            ClassificationControlState.Now.AddSeconds(3))).IsTrue();
        await Assert.That(adoption.MatchesCurrent(selection, state.Source, state.Review, state.Generation,
            adoption.ValidUntil)).IsFalse();
        state.Review.End(1, ModerationClassificationMaterialEnd.Corrected, Guid.NewGuid(), 13,
            "Qualified correction of original review.", ClassificationControlState.Now.AddSeconds(4));
        await Assert.That(adoption.MatchesCurrent(selection, state.Source, state.Review, state.Generation,
            ClassificationControlState.Now.AddSeconds(5))).IsFalse();
    }

    private static ModerationClassificationEventTargetAdoption Adopt(ClassificationControlState state,
        ModerationClassificationControlSelection selection, Guid human) =>
        ModerationClassificationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, 0, state.Generation,
            human, 12, ClassificationControlState.Now.AddSeconds(2), ClassificationControlState.Now.AddMinutes(1));
}
