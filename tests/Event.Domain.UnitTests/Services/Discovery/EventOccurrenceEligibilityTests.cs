using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Services.Scheduling;

namespace Event.Domain.UnitTests.Services.Discovery;

public sealed class EventOccurrenceEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Explicit_open_ended_occurrence_remains_ongoing_after_start()
    {
        var session = Session(Now.AddDays(-60), null, SessionEndTimeType.OpenEnded);

        await Assert.That(EventOccurrenceEligibility.Ongoing(Now).Compile()(session)).IsTrue();
    }

    [Test]
    [Arguments(SessionEndTimeType.Fixed)]
    [Arguments(SessionEndTimeType.RelativeToPrayer)]
    public async Task Unspecified_end_without_explicit_open_ended_is_not_infinity(SessionEndTimeType type)
    {
        var session = Session(Now.AddDays(-1), null, type);

        await Assert.That(EventOccurrenceEligibility.Published().Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.Ongoing(Now).Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.CurrentOrUpcoming(Now).Compile()(session)).IsFalse();
    }

    [Test]
    [Arguments(-1, true, false)]
    [Arguments(0, false, true)]
    [Arguments(1, false, true)]
    public async Task Finite_end_is_exclusive_at_exact_tick(long ticks, bool ongoing, bool past)
    {
        var session = Session(Now.AddHours(-1), Now, SessionEndTimeType.Fixed);
        var instant = Now.AddTicks(ticks).ToOffset(TimeSpan.FromHours(5.5));

        await Assert.That(EventOccurrenceEligibility.Ongoing(instant).Compile()(session)).IsEqualTo(ongoing);
        await Assert.That(EventOccurrenceEligibility.Past(instant).Compile()(session)).IsEqualTo(past);
    }

    [Test]
    public async Task Open_ended_becomes_ongoing_at_start_and_stops_at_recorded_end()
    {
        var session = Session(Now, null, SessionEndTimeType.OpenEnded);
        await Assert.That(EventOccurrenceEligibility.Upcoming(Now.AddTicks(-1)).Compile()(session)).IsTrue();
        await Assert.That(EventOccurrenceEligibility.Ongoing(Now.AddTicks(-1)).Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.Upcoming(Now).Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.Ongoing(Now).Compile()(session)).IsTrue();

        session.EndTime = Now.AddHours(1);
        await Assert.That(EventOccurrenceEligibility.Ongoing(session.EndTime.Value).Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.Past(session.EndTime.Value).Compile()(session)).IsTrue();
    }

    [Test]
    [Arguments(EventSessionStatusEnum.Cancelled)]
    [Arguments(EventSessionStatusEnum.Completed)]
    [Arguments(EventSessionStatusEnum.Archived)]
    [Arguments(EventSessionStatusEnum.Draft)]
    public async Task Nonpublished_occurrences_do_not_contribute(EventSessionStatusEnum status)
    {
        var session = Session(Now.AddDays(-1), null, SessionEndTimeType.OpenEnded);
        session.SynchronizeFederatedLifecycle(status, Now.UtcDateTime);

        await Assert.That(EventOccurrenceEligibility.Published().Compile()(session)).IsFalse();
    }

    [Test]
    public async Task Inclusive_date_window_does_not_join_dates_from_different_occurrences()
    {
        var early = Session(Now.AddYears(-1), Now.AddYears(-1).AddHours(1), SessionEndTimeType.Fixed);
        var late = Session(Now.AddYears(1), Now.AddYears(1).AddHours(1), SessionEndTimeType.Fixed);
        var today = DateOnly.FromDateTime(Now.DateTime);
        var matches = new[] { early, late }.AsQueryable()
            .Where(EventOccurrenceEligibility.Published())
            .Where(EventOccurrenceEligibility.WithinDates(today, today));

        await Assert.That(matches.Any()).IsFalse();
    }

    [Test]
    [Arguments(3, 26, 23)]
    [Arguments(10, 29, 25)]
    public async Task Local_date_intersection_respects_short_and_long_DST_days(int month, int day, int hours)
    {
        var date = new DateOnly(2028, month, day);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), timezone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone));
        var session = Session(start, end, SessionEndTimeType.Fixed);

        await Assert.That((end - start).TotalHours).IsEqualTo((double)hours);
        await Assert.That(EventOccurrenceEligibility.WithinDates(date, date).Compile()(session)).IsTrue();
        await Assert.That(EventOccurrenceEligibility.WithinDates(date.AddDays(1), date.AddDays(1)).Compile()(session)).IsFalse();
        await Assert.That(EventOccurrenceEligibility.WithinDates(date.AddDays(-1), date.AddDays(-1)).Compile()(session)).IsFalse();
    }

    [Test]
    public async Task Open_ended_intersects_later_local_dates_but_not_before_start()
    {
        var session = Session(Now, null, SessionEndTimeType.OpenEnded);
        var date = session.LocalStartDate!.Value;
        await Assert.That(EventOccurrenceEligibility.WithinDates(date.AddYears(1), date.AddYears(1)).Compile()(session)).IsTrue();
        await Assert.That(EventOccurrenceEligibility.WithinDates(null, date.AddDays(-1)).Compile()(session)).IsFalse();
    }

    [Test]
    public async Task Day_requires_published_current_tenant_event_and_local_placement()
    {
        var session = Session(Now, Now.AddHours(1), SessionEndTimeType.Fixed);
        var day = new EventDay
        {
            Id = Guid.CreateVersion7(), EventId = session.EventId, TenantId = session.TenantId,
            Event = null!, Tenant = null!, LocalDate = session.LocalStartDate!.Value, IsPublished = true
        };
        session.EventDayId = day.Id;
        session.EventDay = day;
        var eligible = EventOccurrenceEligibility.Published().Compile();
        await Assert.That(eligible(session)).IsTrue();
        day.IsPublished = false;
        await Assert.That(eligible(session)).IsFalse();
        day.IsPublished = true;
        day.LocalDate = day.LocalDate.AddDays(1);
        await Assert.That(eligible(session)).IsFalse();
        day.LocalDate = session.LocalStartDate.Value;
        day.TenantId = Guid.CreateVersion7();
        await Assert.That(eligible(session)).IsFalse();
        day.TenantId = session.TenantId;
        day.EventId = Guid.CreateVersion7();
        await Assert.That(eligible(session)).IsFalse();
        day.EventId = session.EventId;
        day.IsDeleted = true;
        await Assert.That(eligible(session)).IsFalse();
    }

    [Test]
    public async Task Deleted_and_unscheduled_occurrences_never_match()
    {
        var session = Session(Now, Now.AddHours(1), SessionEndTimeType.Fixed);
        session.IsDeleted = true;
        await Assert.That(EventOccurrenceEligibility.Published().Compile()(session)).IsFalse();
        session.IsDeleted = false;
        session.StartTime = null;
        await Assert.That(EventOccurrenceEligibility.Published().Compile()(session)).IsFalse();
    }

    [Test]
    public async Task Missing_governed_local_projection_cannot_supply_a_matching_card()
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Event = null!, Tenant = null!, StartTime = Now,
            EndTime = Now.AddHours(1), EndTimeType = SessionEndTimeType.Fixed
        };
        await Assert.That(EventOccurrenceEligibility.Published().Compile()(session)).IsFalse();
    }

    private static EventSession Session(DateTimeOffset start, DateTimeOffset? end, SessionEndTimeType type)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Event = null!,
            Tenant = null!,
            StartTime = start,
            EndTime = end,
            EndTimeType = type
        };
        session.ReprojectLocalTimes("Europe/Brussels", new EventScheduleProjectionCalculator());
        return session;
    }
}
