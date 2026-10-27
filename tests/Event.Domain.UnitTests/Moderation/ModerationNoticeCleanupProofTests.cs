namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationNoticeCleanupProofTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ceiling = new(2027, 5, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Cleanup_at_the_original_ceiling_preserves_pending_consequences_and_invalidates_old_leases()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, Id(4), "{\"original\":true}", Ceiling);
        var lease = Guid.CreateVersion7();
        effect.Claim(lease, 1, Ceiling.AddMinutes(-1), Ceiling.AddMinutes(1));
        effect.RecordOriginalNoticeCleanup(source, Id(6), Id(4), Id(7), 2, Ceiling);

        await Assert.That(effect.HasOriginalNoticeCleanup(Id(6), Id(4), Ceiling)).IsTrue();
        await Assert.That(effect.OriginalNoticeRetainUntil).IsEqualTo(Ceiling);
        await Assert.That(effect.OriginalNoticePurgedAt).IsEqualTo(Ceiling);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsEqualTo(lease);
        await Assert.That(effect.Revision).IsEqualTo(3);
        await Assert.That(() => effect.Complete(lease, 2, Ceiling)).Throws<InvalidOperationException>();
        await Assert.That(source.Matches(effect)).IsTrue();
    }

    [Test]
    public async Task Cleanup_before_the_ceiling_cannot_create_a_replay_proof()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, Id(4), "{\"original\":true}", Ceiling);
        await Assert.That(() => effect.RecordOriginalNoticeCleanup(
            source, Id(6), Id(4), Id(7), 1, Ceiling.AddTicks(-1))).Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalNoticeCleanupId).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Cleanup_cannot_be_rebound_renewed_or_borrowed_by_another_tenant_or_host()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, Id(4), "{\"original\":true}", Ceiling);
        effect.RecordOriginalNoticeCleanup(source, Id(6), Id(4), Id(7), 1, Ceiling);
        await Assert.That(effect.HasOriginalNoticeCleanup(Id(8), Id(4), Ceiling)).IsFalse();
        await Assert.That(effect.HasOriginalNoticeCleanup(Id(6), Id(8), Ceiling)).IsFalse();
        await Assert.That(effect.HasOriginalNoticeCleanup(Id(6), Id(4), Ceiling.AddTicks(-1))).IsFalse();
        await Assert.That(() => effect.RecordOriginalNoticeCleanup(
            source, Id(6), Id(4), Id(8), 2, Ceiling.AddDays(1))).Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalNoticeCleanupId).IsEqualTo(Id(7));
        await Assert.That(effect.OriginalNoticePurgedAt).IsEqualTo(Ceiling);
    }

    [Test]
    public async Task Missing_original_capture_and_inert_capture_are_not_cleanup_proofs()
    {
        var effect = Effect();
        await Assert.That(effect.HasOriginalNoticeCleanup(Id(6), Id(4), Ceiling)).IsFalse();
        var inert = ModerationNoticeSource.Capture(effect, Id(4), "{\"inert\":true}", null);
        await Assert.That(() => effect.RecordOriginalNoticeCleanup(
            inert, Id(6), Id(4), Id(7), 1, Ceiling)).Throws<InvalidOperationException>();
        var unrelated = ModerationNoticeSource.Capture(Effect(), Id(4), "{\"original\":true}", Ceiling);
        await Assert.That(() => effect.RecordOriginalNoticeCleanup(
            unrelated, Id(6), Id(4), Id(7), 1, Ceiling)).Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalNoticeCleanupId).IsNull();
    }

    private static ModerationEffectIntent Effect() =>
        ModerationEffectIntent.Create(Guid.CreateVersion7(), Id(1), Id(2), 5,
            null, null, Id(3), 4, Id(4), 1, Id(5), 1, 1, 1, CapturedAt, null, null);

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
