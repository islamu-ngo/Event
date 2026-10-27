namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationNoticeRetentionTests
{
    [Test]
    public async Task Notification_uses_forward_calendar_arithmetic_without_extending_retention()
    {
        var retainUntil = new DateTimeOffset(2027, 2, 28, 12, 0, 0, TimeSpan.Zero);
        var notifiedAt = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(notifiedAt, 6, retainUntil)).IsTrue();
        await Assert.That(notifiedAt.AddMonths(6)).IsEqualTo(retainUntil);
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(notifiedAt.AddDays(1), 6, retainUntil)).IsFalse();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(121)]
    public async Task Notification_cannot_invent_an_unapproved_calendar_floor(int months)
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(now, months, now.AddYears(1))).IsFalse();
    }

    [Test]
    public async Task Zero_month_profile_still_requires_finite_unexpired_retention()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(now, 0, now.AddDays(1))).IsTrue();
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(now, 0, now)).IsFalse();
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(now, 0, DateTimeOffset.MaxValue)).IsFalse();
    }

    [Test]
    public async Task Notification_rejects_non_utc_and_overflowing_calendar_bounds()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(
            now.ToOffset(TimeSpan.FromHours(1)), 6, now.AddYears(1))).IsFalse();
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(
            now, 6, now.AddYears(1).ToOffset(TimeSpan.FromHours(1)))).IsFalse();
        await Assert.That(ModerationNoticeRetention.CanNotifyAt(
            new DateTimeOffset(9999, 8, 31, 12, 0, 0, TimeSpan.Zero), 6,
            new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero))).IsFalse();
    }
}
