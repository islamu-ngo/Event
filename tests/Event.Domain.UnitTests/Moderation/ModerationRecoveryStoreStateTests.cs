namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecoveryStoreStateTests
{
    [Test]
    public async Task A_new_store_has_its_own_identity_and_no_completed_recovery_position()
    {
        var id = Guid.CreateVersion7();
        var state = ModerationRecoveryStoreState.Start(id);
        await Assert.That(state.Id).IsEqualTo(id);
        await Assert.That(state.Revision).IsEqualTo(1);
        await Assert.That(state.Generation).IsEqualTo(1);
        await Assert.That(state.Position).IsEqualTo(0);
        await Assert.That(ModerationRecoveryStoreState.Start(Guid.CreateVersion7()).Id).IsNotEqualTo(id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Store_identity_must_be_native_uuid_v7(bool empty)
    {
        await Assert.That(() => ModerationRecoveryStoreState.Start(empty ? Guid.Empty : Guid.NewGuid()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Actual_writes_advance_contiguously_without_changing_store_or_generation()
    {
        var state = ModerationRecoveryStoreState.Start(Guid.CreateVersion7());
        state.Advance(1, 0);
        state.Advance(2, 1);
        await Assert.That(state.Revision).IsEqualTo(3);
        await Assert.That(state.Position).IsEqualTo(2);
        await Assert.That(state.Generation).IsEqualTo(1);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(1, 1)]
    [Arguments(2, 0)]
    public async Task A_stale_or_substituted_write_cannot_advance_the_store(long revision, long position)
    {
        var state = ModerationRecoveryStoreState.Start(Guid.CreateVersion7());
        await Assert.That(() => state.Advance(revision, position)).Throws<InvalidOperationException>();
        await Assert.That(state.Revision).IsEqualTo(1);
        await Assert.That(state.Position).IsEqualTo(0);
        await Assert.That(state.Generation).IsEqualTo(1);
    }

    [Test]
    public async Task Recovery_changes_generation_without_rewinding_committed_position()
    {
        var state = ModerationRecoveryStoreState.Start(Guid.CreateVersion7());
        state.Advance(1, 0);
        state.BeginRecoveryGeneration(2, 1, 1);
        await Assert.That(state.Revision).IsEqualTo(3);
        await Assert.That(state.Generation).IsEqualTo(2);
        await Assert.That(state.Position).IsEqualTo(1);
        await Assert.That(() => state.BeginRecoveryGeneration(2, 1, 1)).Throws<InvalidOperationException>();
        state.Advance(3, 1);
        await Assert.That(state.Position).IsEqualTo(2);
        await Assert.That(state.Generation).IsEqualTo(2);
    }
}
