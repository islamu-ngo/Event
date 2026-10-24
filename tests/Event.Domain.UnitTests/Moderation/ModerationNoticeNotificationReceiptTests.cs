namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationNoticeNotificationReceiptTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 1, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ceiling = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ContentAcknowledgementKeepsTheObjectSubjectAndRecordsTheActualRepresentative()
    {
        var human = Guid.CreateVersion7();
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            2, null, null, Guid.CreateVersion7(), 6, Guid.CreateVersion7(), 4, Guid.CreateVersion7(),
            1, 1, 1, CapturedAt, null, null);
        var original = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 2,
            WriterGeneration = "event.moderation.capture.v2",
            Decision = new { SubjectKind = 4, SubjectId = effect.SubjectId },
            ContentRepresentatives = new[] { new ModerationNoticeContentRepresentative(Guid.CreateVersion7(), 2, human) }
        });
        var source = ModerationNoticeSource.Capture(effect, Guid.CreateVersion7(), original, Ceiling);
        var notice = ModerationNotice.Create(Guid.CreateVersion7(), source,
            ModerationNoticeAudience.ContentRepresentative, 1, human, null,
            ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
        var receipt = ModerationNoticeNotificationReceipt.Acknowledge(Guid.CreateVersion7(), notice, source,
            source.TenantId!.Value, human, source.SnapshotHash, 1, Ceiling, CapturedAt);
        await Assert.That(receipt.SubjectId).IsEqualTo(effect.SubjectId);
        await Assert.That(receipt.SubjectKind).IsEqualTo(4);
        await Assert.That(receipt.AcknowledgedByHumanId).IsEqualTo(human);
        await Assert.That(receipt.Matches(source, source.TenantId.Value, 1, Ceiling)).IsTrue();
        await Assert.That(receipt.RetainUntil).IsEqualTo(Ceiling);
        await Assert.That(() => ModerationNotice.Create(Guid.CreateVersion7(), source,
            ModerationNoticeAudience.ContentRepresentative, 1, Guid.CreateVersion7(), null,
            ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)).Throws<ArgumentException>();
        await Assert.That(() => ModerationNoticeNotificationReceipt.Acknowledge(Guid.CreateVersion7(), notice,
            source, source.TenantId.Value, effect.SubjectId, source.SnapshotHash, 1, Ceiling, CapturedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExplicitNotificationUsesForwardCalendarMonthsRatherThanCaptureTimeOrFixedDays()
    {
        var (source, notice) = Original();
        var notifiedAt = new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
        var receipt = ModerationNoticeNotificationReceipt.Acknowledge(
            Guid.CreateVersion7(), notice, source, source.TenantId!.Value, source.SubjectId,
            source.SnapshotHash, 1, Ceiling, notifiedAt);
        await Assert.That(receipt.NotifiedAt).IsEqualTo(notifiedAt);
        await Assert.That(receipt.RemedyUntil).IsEqualTo(new DateTimeOffset(2026, 2, 28, 12, 0, 0, TimeSpan.Zero));
        await Assert.That(receipt.RetainUntil).IsEqualTo(Ceiling);
        await Assert.That(receipt.Matches(source, source.TenantId.Value, 1, Ceiling)).IsTrue();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("hash")]
    [Arguments("tenant")]
    [Arguments("ceiling")]
    [Arguments("late")]
    [Arguments("reviewer")]
    public async Task ChangedSubjectProofScopeOrFiniteCeilingCannotCreateAReceipt(string substitution)
    {
        var (source, notice) = Original();
        if (substitution == "reviewer")
            notice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                1, Guid.CreateVersion7(), null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
        await Assert.That(() => ModerationNoticeNotificationReceipt.Acknowledge(
            Guid.CreateVersion7(), notice, source,
            substitution == "tenant" ? Guid.CreateVersion7() : source.TenantId!.Value,
            substitution == "subject" ? Guid.CreateVersion7() : source.SubjectId,
            substitution == "hash" ? new string('a', 64) : source.SnapshotHash,
            6, substitution == "ceiling" ? Ceiling.AddMonths(1) : Ceiling,
            substitution == "late" ? new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1) : CapturedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ZeroMonthProfileDoesNotInventAMandatorySixMonthFloor()
    {
        var (source, notice) = Original();
        var receipt = ModerationNoticeNotificationReceipt.Acknowledge(
            Guid.CreateVersion7(), notice, source, source.TenantId!.Value, source.SubjectId,
            source.SnapshotHash, 0, Ceiling, CapturedAt);
        await Assert.That(receipt.RemedyUntil).IsEqualTo(CapturedAt);
    }

    private static (ModerationNoticeSource Source, ModerationNotice Notice) Original()
    {
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            2, null, null, Guid.CreateVersion7(), 3, Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
            1, 1, 1, CapturedAt, null, null);
        var source = ModerationNoticeSource.Capture(effect, Guid.CreateVersion7(), "{\"Version\":1}", Ceiling);
        var notice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
            1, source.SubjectId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
        return (source, notice);
    }
}
