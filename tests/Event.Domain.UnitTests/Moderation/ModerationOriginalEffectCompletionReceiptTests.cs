namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationOriginalEffectCompletionReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Receipt_cannot_complete_or_extend_the_effect_by_itself()
    {
        var (effect, source, lease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(receipt.MatchesCompletion(effect)).IsFalse();
        effect.Complete(lease, effect.Revision, Now);
        await Assert.That(receipt.MatchesCompletion(effect)).IsTrue();
        await Assert.That(receipt.RetainUntil).IsEqualTo(source.RetainUntil!.Value);
    }

    [Test]
    public async Task Equal_completion_time_on_another_effect_is_not_replay()
    {
        var (effect, source, lease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        var (other, _, otherLease) = Original();
        other.Complete(otherLease, other.Revision, Now);
        await Assert.That(receipt.MatchesCompletion(other)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(65)]
    public async Task Receipt_requires_every_original_ordinal_within_the_capture_bound(int count)
    {
        var (effect, source, lease) = Original();
        await Assert.That(() => ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), count, Now, Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Expired_original_custody_cannot_be_replaced_by_a_later_deadline()
    {
        var (effect, source, lease) = Original(Now.AddMinutes(1));
        await Assert.That(() => ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, source.RetainUntil!.Value, Now.AddHours(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Receipt_rejects_the_exact_half_open_lease_deadline()
    {
        var (effect, source, lease) = Original();
        await Assert.That(() => ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now.AddMinutes(2), Now.AddMinutes(2)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Receipt_cannot_bind_another_original_capture_to_the_owned_effect()
    {
        var (effect, _, lease) = Original();
        var (_, foreign, _) = Original();
        await Assert.That(() => ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, foreign,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Recovered_lease_cannot_borrow_prior_completion_coordinates()
    {
        var (effect, source, lease) = Original();
        var revision = effect.Revision;
        effect.Release(lease, revision, Now);
        effect.Claim(Guid.CreateVersion7(), effect.Revision, Now, Now.AddMinutes(2));
        await Assert.That(() => ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1)))
            .Throws<ArgumentException>();
    }

    private static (ModerationEffectIntent Effect, ModerationNoticeSource Source, Guid Lease) Original(
        DateTimeOffset? retainUntil = null)
    {
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 4, Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 6,
            Guid.CreateVersion7(), 4, Guid.CreateVersion7(), 5, 1, 1, Now, null, null);
        var source = ModerationNoticeSource.Capture(effect, Guid.CreateVersion7(), "{}", retainUntil ?? Now.AddMinutes(3));
        var lease = Guid.CreateVersion7();
        effect.Claim(lease, 1, Now, Now.AddMinutes(2));
        return (effect, source, lease);
    }

    [Test]
    public async Task Exact_original_completion_acknowledgement_survives_later_original_custody_cleanup()
    {
        var (effect, source, lease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        effect.Complete(lease, effect.Revision, Now);
        effect.RecordOriginalCompletionAcknowledgement(receipt);
        var completedRevision = effect.Revision;

        effect.RecordOriginalNoticeCleanup(source, receipt.HostBindingId, receipt.TenantId,
            Guid.CreateVersion7(), effect.Revision, source.RetainUntil!.Value);

        await Assert.That(effect.HasOriginalCompletionAcknowledgement(receipt.HostBindingId, receipt.TenantId)).IsTrue();
        await Assert.That(effect.OriginalCompletionId).IsEqualTo(receipt.Id);
        await Assert.That(effect.OriginalCompletionRevision).IsEqualTo(completedRevision);
        await Assert.That(effect.Revision).IsEqualTo(completedRevision + 1);
        await Assert.That(effect.CompletedAt).IsEqualTo(Now);
        await Assert.That(effect.LeaseExpiresAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(receipt.MatchesCompletion(effect)).IsFalse();
    }

    [Test]
    public async Task A_receipt_cannot_acknowledge_unfinished_original_work()
    {
        var (effect, source, lease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        await Assert.That(() => effect.RecordOriginalCompletionAcknowledgement(receipt))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalCompletionId).IsNull();
        await Assert.That(effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task A_foreign_completion_receipt_cannot_acknowledge_another_completed_effect()
    {
        var (effect, _, lease) = Original();
        var (other, otherSource, otherLease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), other, otherSource,
            Guid.CreateVersion7(), otherLease, other.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        effect.Complete(lease, effect.Revision, Now);
        await Assert.That(() => effect.RecordOriginalCompletionAcknowledgement(receipt))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalCompletionId).IsNull();
    }

    [Test]
    public async Task Original_completion_acknowledgement_does_not_transfer_host_or_writer_tenant_ownership()
    {
        var (effect, source, lease) = Original();
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            Guid.CreateVersion7(), lease, effect.Revision, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), 1, Now, Now.AddMinutes(1));
        effect.Complete(lease, effect.Revision, Now);
        effect.RecordOriginalCompletionAcknowledgement(receipt);

        await Assert.That(effect.HasOriginalCompletionAcknowledgement(Guid.CreateVersion7(), receipt.TenantId)).IsFalse();
        await Assert.That(effect.HasOriginalCompletionAcknowledgement(receipt.HostBindingId, Guid.CreateVersion7())).IsFalse();
        await Assert.That(() => effect.RecordOriginalCompletionAcknowledgement(receipt))
            .Throws<InvalidOperationException>();
        await Assert.That(effect.OriginalCompletionId).IsEqualTo(receipt.Id);
        await Assert.That(effect.Revision).IsEqualTo(3);
    }
}
