namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionEscalationTests
{
    [Test]
    public async Task An_obligation_is_not_delivery_and_another_owner_cannot_acknowledge_it()
    {
        var (pending, source, _) = ModerationRecognitionDispositionTests.Fixture();
        var now = ModerationRecognitionDispositionTests.Now;
        var escalation = ModerationRecognitionEscalation.Require(Guid.CreateVersion7(), pending, null,
            source, pending.PrivacyOwnerHumanId, now);
        await Assert.That(escalation.NotificationId).IsNull();
        await Assert.That(escalation.DeliveredAt).IsNull();
        await Assert.That(() => escalation.Acknowledge(pending.PrivacyOwnerHumanId, now))
            .Throws<InvalidOperationException>();
        escalation.Deliver(Guid.CreateVersion7(), now);
        await Assert.That(() => escalation.Acknowledge(pending.RecoveryOwnerHumanId, now))
            .Throws<InvalidOperationException>();
        escalation.Acknowledge(pending.PrivacyOwnerHumanId, now);
        await Assert.That(escalation.AcknowledgedAt).IsEqualTo(now);
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.MaterialReceipt).IsNull();
    }

    [Test]
    public async Task Deadline_stage_and_original_retention_do_not_restart_on_retry()
    {
        var (pending, source, _) = ModerationRecognitionDispositionTests.Fixture();
        var original = ModerationRecognitionEscalation.Require(Guid.CreateVersion7(), pending, null,
            source, pending.PrivacyOwnerHumanId, ModerationRecognitionDispositionTests.Now);
        var overdue = ModerationRecognitionEscalation.Require(Guid.CreateVersion7(), pending, null,
            source, pending.PrivacyOwnerHumanId, pending.SettleBy);
        await Assert.That(original.Stage).IsEqualTo(ModerationRecognitionEscalationStage.OwnerActionRequired);
        await Assert.That(overdue.Stage).IsEqualTo(ModerationRecognitionEscalationStage.LawfulDeadlineReached);
        await Assert.That(overdue.OriginalDeadline).IsEqualTo(original.OriginalDeadline);
        await Assert.That(overdue.RetainUntil).IsEqualTo(original.RetainUntil);
    }
}
