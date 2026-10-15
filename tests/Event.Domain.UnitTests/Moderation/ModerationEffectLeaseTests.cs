namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationEffectLeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid First = Guid.Parse("0199a333-3333-7333-8333-333333333331");
    private static readonly Guid Second = Guid.Parse("0199a333-3333-7333-8333-333333333332");

    [Test]
    public async Task Completion_requires_a_current_owned_lease()
    {
        var effect = Effect();
        await Assert.That(() => effect.Complete(First, 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task A_live_owner_excludes_competing_claims()
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        await Assert.That(() => effect.Claim(Second, 2, Now.AddSeconds(1), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.LeaseId).IsEqualTo(First);
        await Assert.That(effect.Revision).IsEqualTo(2);
        await Assert.That(effect.CompletedAt).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reclaimed_expiry_rejects_old_owner_even_with_a_new_revision(bool currentRevision)
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        effect.Claim(Second, 2, Now.AddMinutes(1), Now.AddMinutes(3));
        await Assert.That(() => effect.Complete(First, currentRevision ? 3 : 2, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        effect.Complete(Second, 3, Now.AddMinutes(2));
        await Assert.That(effect.CompletedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(effect.Revision).IsEqualTo(4);
        effect.ValidatePersistedState();
    }

    [Test]
    [Arguments("expired")]
    [Arguments("clock-rollback")]
    [Arguments("other-owner")]
    [Arguments("stale-revision")]
    public async Task Invalid_completion_leaves_the_owned_intent_unchanged(string change)
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        var at = change == "expired" ? Now.AddMinutes(1)
            : change == "clock-rollback" ? Now.AddTicks(-1) : Now.AddSeconds(1);
        await Assert.That(() => effect.Complete(change == "other-owner" ? Second : First,
            change == "stale-revision" ? 1 : 2, at)).Throws<InvalidOperationException>();
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsEqualTo(First);
        await Assert.That(effect.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Release_cannot_be_replayed_over_a_successor_claim()
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        effect.Release(First, 2, Now.AddSeconds(1));
        await Assert.That(effect.LeaseId).IsNull();
        await Assert.That(effect.LeaseStartedAt).IsNull();
        await Assert.That(effect.LeaseExpiresAt).IsNull();
        effect.Claim(Second, 3, Now.AddSeconds(2), Now.AddMinutes(1));
        await Assert.That(() => effect.Release(First, 2, Now.AddSeconds(3)))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.LeaseId).IsEqualTo(Second);
        await Assert.That(effect.Revision).IsEqualTo(4);
    }

    [Test]
    public async Task An_expired_lease_can_be_released_without_completing_the_work()
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        effect.Release(First, 2, Now.AddMinutes(2));
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(3);
        effect.ValidatePersistedState();
    }

    [Test]
    [Arguments("empty-lease")]
    [Arguments("non-native-lease")]
    [Arguments("invalid-window")]
    public async Task Invalid_claim_coordinates_do_not_mutate_the_intent(string change)
    {
        var effect = Effect();
        var id = change == "empty-lease" ? Guid.Empty : change == "non-native-lease"
            ? Guid.Parse("33333333-3333-4333-8333-333333333333") : First;
        await Assert.That(() => effect.Claim(id, 1, Now,
            change == "invalid-window" ? Now : Now.AddMinutes(1))).Throws<ArgumentException>();
        await Assert.That(effect.LeaseId).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Reclaim_requires_a_fresh_fencing_identity()
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        await Assert.That(() => effect.Claim(First, 2, Now.AddMinutes(1), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Completed_work_cannot_be_claimed_or_released()
    {
        var effect = Effect();
        effect.Claim(First, 1, Now, Now.AddMinutes(1));
        effect.Complete(First, 2, Now.AddSeconds(1));
        await Assert.That(() => effect.Claim(Second, 3, Now.AddSeconds(2), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => effect.Release(First, 3, Now.AddSeconds(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.Revision).IsEqualTo(3);
    }

    private static ModerationEffectIntent Effect() => ModerationEffectIntent.Create(
        Guid.Parse("0199a333-3333-7333-8333-333333333333"),
        Guid.Parse("0199a333-3333-7333-8333-333333333334"),
        Guid.Parse("0199a333-3333-7333-8333-333333333335"), 1, null, null,
        Guid.Parse("0199a333-3333-7333-8333-333333333336"), 4,
        Guid.Parse("0199a333-3333-7333-8333-333333333337"), 1,
        Guid.Parse("0199a333-3333-7333-8333-333333333338"), 1, 1, 1, Now, null, null);
}
