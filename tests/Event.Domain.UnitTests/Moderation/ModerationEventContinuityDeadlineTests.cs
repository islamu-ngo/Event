using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationEventContinuityDeadlineTests
{
    private static readonly DateTimeOffset Activated = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(-24, -26)]
    [Arguments(1, -1)]
    [Arguments(2, 0)]
    [Arguments(12, 10)]
    [Arguments(24, 22)]
    [Arguments(48, 24)]
    [Arguments(72, 24)]
    public async Task Fallback_is_due_at_the_earlier_original_activation_or_event_boundary(int startHours, int dueHours)
    {
        var deadline = ModerationEventContinuityDeadline.Create(Activated, Activated.AddHours(startHours));
        await Assert.That(deadline.DueAt).IsEqualTo(Activated.AddHours(dueHours));
        await Assert.That(deadline.HasKnownSchedule).IsTrue();
        await Assert.That(deadline.IsDue(Activated)).IsEqualTo(dueHours <= 0);
    }

    [Test]
    public async Task Missing_schedule_cannot_certify_timely_fallback_and_requires_immediate_escalation()
    {
        var deadline = ModerationEventContinuityDeadline.Create(Activated, null);
        await Assert.That(deadline.HasKnownSchedule).IsFalse();
        await Assert.That(deadline.DueAt).IsEqualTo(Activated);
        await Assert.That(deadline.IsDue(Activated)).IsTrue();
    }

    [Test]
    public async Task Exact_deadline_is_due_and_later_observations_never_renew_it()
    {
        var deadline = ModerationEventContinuityDeadline.Create(Activated, Activated.AddDays(3));
        await Assert.That(deadline.IsDue(Activated.AddDays(1).AddTicks(-1))).IsFalse();
        await Assert.That(deadline.IsDue(Activated.AddDays(1))).IsTrue();
        await Assert.That(deadline.IsDue(Activated.AddDays(2))).IsTrue();
        await Assert.That(deadline.DueAt).IsEqualTo(Activated.AddDays(1));
    }

    [Test]
    [Arguments("activation-offset")]
    [Arguments("start-offset")]
    [Arguments("activation-overflow")]
    [Arguments("activation-limit")]
    [Arguments("start-underflow")]
    [Arguments("start-limit")]
    public async Task Noncanonical_or_unrepresentable_clock_boundaries_cannot_create_a_fallback_deadline(string change)
    {
        var activation = change switch
        {
            "activation-offset" => Activated.ToOffset(TimeSpan.FromHours(1)),
            "activation-overflow" => DateTimeOffset.MaxValue.AddHours(-12),
            "activation-limit" => DateTimeOffset.MaxValue.AddHours(-24),
            _ => Activated
        };
        var start = change switch
        {
            "start-offset" => Activated.AddDays(3).ToOffset(TimeSpan.FromHours(1)),
            "start-underflow" => DateTimeOffset.MinValue.AddHours(1),
            "start-limit" => DateTimeOffset.MinValue.AddHours(2),
            _ => Activated.AddDays(3)
        };
        await Assert.That(() => ModerationEventContinuityDeadline.Create(activation, start))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Regressing_or_noncanonical_observation_is_not_a_timeliness_proof()
    {
        var deadline = ModerationEventContinuityDeadline.Create(Activated, Activated.AddDays(3));
        await Assert.That(() => deadline.IsDue(Activated.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => deadline.IsDue(Activated.ToOffset(TimeSpan.FromHours(1)))).Throws<ArgumentException>();
    }
}
