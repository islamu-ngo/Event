namespace Event.Domain.UnitTests.Entities;

using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

public class EventScheduleProjectionTests
{
    private readonly EventScheduleProjectionCalculator _calculator = new();

    [Test]
    public async Task ApplyScheduleTimeZone_ReprojectsChildrenAndRelinksMatchingEventDays()
    {
        var eventId = Guid.NewGuid();
        var tenant = CreateTenant();
        var day = new EventDay
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            LocalDate = new DateOnly(2026, 6, 15)
        };
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            StartTime = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };
        var agendaItem = new EventAgendaItem
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            Title = "Dhuhr break",
            StartTime = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)
        };
        var @event = CreateEvent(eventId, tenant);
        @event.Days.Add(day);
        @event.Sessions.Add(session);
        @event.AgendaItems.Add(agendaItem);

        @event.ApplyScheduleTimeZone("Europe/Brussels", _calculator);

        await Assert.That(@event.EventTimeZoneId).IsEqualTo("Europe/Brussels");
        await Assert.That(@event.Timezone).IsEqualTo("Europe/Brussels");
        await Assert.That(session.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(agendaItem.LocalStartTime).IsEqualTo(new TimeOnly(11, 30));
        await Assert.That(session.EventDayId).IsEqualTo(day.Id);
        await Assert.That(agendaItem.EventDayId).IsEqualTo(day.Id);
        await Assert.That(@event.FirstSessionStartUtc).IsEqualTo(session.StartTime);
        await Assert.That(@event.LastSessionStartUtc).IsEqualTo(session.StartTime);
        await Assert.That(@event.FirstSessionDate).IsEqualTo(day.LocalDate);
        await Assert.That(@event.SessionCount).IsEqualTo(1);
    }

    [Test]
    public async Task ApplyScheduleTimeZone_WhenTimezoneShiftsLocalDate_DropsStaleDayLink()
    {
        var eventId = Guid.NewGuid();
        var tenant = CreateTenant();
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            EventDayId = Guid.NewGuid(),
            StartTime = new DateTimeOffset(2026, 6, 15, 22, 30, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero)
        };
        var @event = CreateEvent(eventId, tenant);
        @event.Days.Add(new EventDay
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            LocalDate = new DateOnly(2026, 6, 15)
        });
        @event.Sessions.Add(session);

        @event.ApplyScheduleTimeZone("Europe/Brussels", _calculator);

        await Assert.That(session.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 16));
        await Assert.That(session.EventDayId).IsNull();
        await Assert.That(@event.FirstSessionDate).IsEqualTo(new DateOnly(2026, 6, 16));
    }

    [Test]
    public async Task RecalculateScheduleSummaryFromSessions_UsesOnlyPublishedScheduledSessions()
    {
        var eventId = Guid.NewGuid();
        var tenant = CreateTenant();
        var published = CreateSession(eventId, tenant, EventSessionStatusEnum.Published, 3, "Europe/Brussels");
        var draftEarlier = CreateSession(eventId, tenant, EventSessionStatusEnum.Draft, 1, "Europe/Brussels");
        var rejectedLater = CreateSession(eventId, tenant, EventSessionStatusEnum.Rejected, 5, "Europe/Brussels");
        var unscheduledPublished = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant
        };
        var @event = CreateEvent(eventId, tenant);
        @event.Sessions.Add(draftEarlier);
        @event.Sessions.Add(published);
        @event.Sessions.Add(rejectedLater);
        @event.Sessions.Add(unscheduledPublished);

        @event.RecalculateScheduleSummaryFromSessions();

        await Assert.That(@event.SessionCount).IsEqualTo(1);
        await Assert.That(@event.FirstSessionStartUtc).IsEqualTo(published.StartTime);
        await Assert.That(@event.LastSessionStartUtc).IsEqualTo(published.StartTime);
        await Assert.That(@event.FirstSessionDate).IsEqualTo(published.LocalStartDate);
        await Assert.That(@event.LastSessionDate).IsEqualTo(published.LocalStartDate);
    }

    [Test]
    public async Task RecalculateScheduleSummaryFromSessions_WhenEarlierSessionEndsLast_UsesMaximumEnd()
    {
        var eventId = Guid.NewGuid();
        var tenant = CreateTenant();
        var earlier = CreateSession(eventId, tenant, EventSessionStatusEnum.Published, 0, "Europe/Brussels");
        var later = CreateSession(eventId, tenant, EventSessionStatusEnum.Published, 1, "Europe/Brussels");
        earlier.EndTime = later.EndTime!.Value.AddHours(1);
        earlier.ReprojectLocalTimes("Europe/Brussels", _calculator);
        var @event = CreateEvent(eventId, tenant);
        @event.Sessions.Add(later);
        @event.Sessions.Add(earlier);

        @event.RecalculateScheduleSummaryFromSessions();

        await Assert.That(@event.FirstSessionStartUtc).IsEqualTo(earlier.StartTime);
        await Assert.That(@event.LastSessionStartUtc).IsEqualTo(later.StartTime);
        await Assert.That(@event.LastSessionEndUtc).IsEqualTo(earlier.EndTime);
    }

    [Test]
    public async Task RecalculateScheduleSummaryFromSessions_WhenAnySessionIsOpenEnded_ClearsLastEnd()
    {
        var eventId = Guid.NewGuid();
        var tenant = CreateTenant();
        var openEnded = CreateSession(eventId, tenant, EventSessionStatusEnum.Published, 0, "Europe/Brussels");
        openEnded.EndTime = null;
        openEnded.EndTimeType = SessionEndTimeType.OpenEnded;
        openEnded.ReprojectLocalTimes("Europe/Brussels", _calculator);
        var later = CreateSession(eventId, tenant, EventSessionStatusEnum.Published, 1, "Europe/Brussels");
        var @event = CreateEvent(eventId, tenant);
        @event.Sessions.Add(openEnded);
        @event.Sessions.Add(later);

        @event.RecalculateScheduleSummaryFromSessions();

        await Assert.That(@event.FirstSessionStartUtc).IsEqualTo(openEnded.StartTime);
        await Assert.That(@event.LastSessionStartUtc).IsEqualTo(later.StartTime);
        await Assert.That(@event.LastSessionEndUtc).IsNull();
    }

    [Test]
    public async Task Reschedule_WhenInputHasNonUtcOffset_StoresUtcInstantsAndPreservesConfiguredLocalProjection()
    {
        var tenant = CreateTenant();
        var localStart = new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.FromHours(2));
        var localEnd = new DateTimeOffset(2026, 7, 10, 20, 0, 0, TimeSpan.FromHours(2));
        var session = new EventSession(EventSessionStatusEnum.Draft)
        {
            Event = null!,
            Tenant = tenant
        };
        var agendaItem = new EventAgendaItem
        {
            Event = null!,
            Tenant = tenant,
            Title = "Arrival"
        };

        session.Reschedule(UtcInstantRange.Create(localStart, localEnd), "Europe/Brussels", _calculator);
        agendaItem.Reschedule(UtcInstantRange.Create(localStart, localEnd), "Europe/Brussels", _calculator);

        await Assert.That(session.StartTime).IsEqualTo(localStart.ToUniversalTime());
        await Assert.That(session.EndTime).IsEqualTo(localEnd.ToUniversalTime());
        await Assert.That(session.StartTime!.Value.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(session.EndTime!.Value.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(session.LocalStartTime).IsEqualTo(new TimeOnly(18, 0));
        await Assert.That(session.LocalEndTime).IsEqualTo(new TimeOnly(20, 0));
        await Assert.That(agendaItem.StartTime).IsEqualTo(localStart.ToUniversalTime());
        await Assert.That(agendaItem.EndTime).IsEqualTo(localEnd.ToUniversalTime());
        await Assert.That(agendaItem.StartTime.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(agendaItem.EndTime.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(agendaItem.LocalStartTime).IsEqualTo(new TimeOnly(18, 0));
        await Assert.That(agendaItem.LocalEndTime).IsEqualTo(new TimeOnly(20, 0));
    }

    private EventSession CreateSession(Guid eventId, Tenant tenant, EventSessionStatusEnum status, int dayOffset, string timezoneId)
    {
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero).AddDays(dayOffset);
        var session = new EventSession(status)
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Tenant = tenant,
            StartTime = start,
            EndTime = start.AddHours(1)
        };
        session.ReprojectLocalTimes(timezoneId, _calculator);
        return session;
    }

    private static Explore.Domain.Event CreateEvent(Guid eventId, Tenant tenant) => new()
    {
        Id = eventId,
        Title = "Event",
        Actor = null!,
        Tenant = tenant,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static Tenant CreateTenant() => new()
    {
        FullName = "Tenant",
        Slug = "tenant",
        TenantStatusId = 2,
        TenantStatus = new TenantStatus { Id = 2, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true }
    };
}
