namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationProfileAdoptionTests
{
    [Test]
    public async Task IndependentProfileAdoption_RetainsExactModePrerequisitesAndTerminalRevocation()
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        var adoption = ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection, 1, 17,
            Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        var revoker = Guid.CreateVersion7();
        adoption.Revoke(1, revoker, selection.RecordedAt.AddMinutes(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.SelectionId).IsEqualTo(selection.Id);
        await Assert.That(adoption.SelectionWriterGeneration).IsEqualTo(selection.WriterGeneration);
        await Assert.That(adoption.PublicationId).IsEqualTo(selection.PublicationId);
        await Assert.That(adoption.ProfileId).IsEqualTo(selection.ProfileId);
        await Assert.That(adoption.ModeCode).IsEqualTo(1);
        await Assert.That(adoption.PrerequisiteMask).IsEqualTo(17);
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(revoker);
        await Assert.That(() => adoption.Revoke(2, revoker, selection.RecordedAt.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task InvalidProfileAdoption_RejectsSelfApprovalUnknownModeMissingBaselineAndInvalidWindow(int change)
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        await Assert.That(() => ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection,
            change == 1 ? 0 : 1, change == 2 ? 1 : 17,
            change == 0 ? selection.AuthoredByHumanId : Guid.CreateVersion7(), 4, selection.RecordedAt,
            change switch
            {
                3 => selection.RecordedAt,
                4 => selection.ValidUntil.AddTicks(1),
                _ => selection.RecordedAt.AddMinutes(5)
            })).Throws<ArgumentException>();
    }
    [Test]
    [Arguments(0L)]
    [Arguments(2L)]
    [Arguments(long.MaxValue)]
    public async Task Adoption_generation_must_follow_the_original_selection(long generation)
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        await Assert.That(() => ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection,
            1, 17, Guid.CreateVersion7(), generation, selection.RecordedAt, selection.RecordedAt.AddMinutes(5)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Adoption_requires_a_canonical_human_identity()
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        await Assert.That(() => ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection,
            1, 17, Guid.NewGuid(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Malformed_revoker_cannot_partially_retire_an_adoption()
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        var adoption = ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection, 1, 17,
            Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));

        await Assert.That(() => adoption.Revoke(1, Guid.NewGuid(), selection.RecordedAt))
            .Throws<ArgumentException>();
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(1L);
        await Assert.That(adoption.RevokedAt).IsNull();
        await Assert.That(adoption.RevokedByHumanId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hydrated_adoption_and_revocation_require_canonical_humans(bool revoked)
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        var adoption = ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection, 1, 17,
            Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        if (revoked)
            adoption.Revoke(1, Guid.CreateVersion7(), selection.RecordedAt);
        typeof(ModerationProfileAdoption).GetProperty(revoked
            ? nameof(adoption.RevokedByHumanId) : nameof(adoption.AdoptedByHumanId))!
            .SetValue(adoption, Guid.NewGuid());

        await Assert.That(adoption.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    public async Task Hydrated_adoption_cannot_reuse_its_selection_generation()
    {
        var selection = ModerationContributionSelectionTests.Selection().Selection;
        var adoption = ModerationProfileAdoption.Adopt(Guid.CreateVersion7(), selection, 1, 17,
            Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        typeof(ModerationProfileAdoption).GetProperty(nameof(adoption.WriterGeneration))!
            .SetValue(adoption, selection.WriterGeneration);

        await Assert.That(adoption.ValidatePersistedState).Throws<ArgumentException>();
    }
}
