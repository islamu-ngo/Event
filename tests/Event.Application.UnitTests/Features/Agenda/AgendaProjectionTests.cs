using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Agenda;
using Explore.Application.Features.Agenda.Handlers.Queries;
using Explore.Application.Features.Agenda.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Agenda;

public sealed class AgendaProjectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Merge_UsesLocalMinutesThenSortAndDayPriorityBeforeDate(bool reverseInput)
    {
        var parent = Parent();
        parent.ApplyScheduleTimeZone("Europe/Brussels", new EventScheduleProjectionCalculator());
        var laterDay = Day(parent, 23, -1);
        laterDay.Label = "Opening day";
        laterDay.Description = "Published description";
        laterDay.AllowsDayScopeRegistration = true;
        var firstDay = Day(parent, 21, 2);
        var hiddenDay = Day(parent, 20, -2);
        hiddenDay.IsPublished = false;
        List<EventDay> days = [firstDay, hiddenDay, laterDay];
        var early = Session(parent, "Local early", 20, 23, 30, 90);
        var tie = Session(parent, "Tie session", 21, 7, 0, 5);
        var unscheduled = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = parent.TenantId, Tenant = null!, EventId = parent.Id,
            Event = parent, Title = "Unscheduled"
        };
        var incomplete = Session(parent, "Missing end", 21, 8, 0, 0);
        incomplete.EndTime = null;
        incomplete.ReprojectLocalTimes("Europe/Brussels", new EventScheduleProjectionCalculator());
        List<EventSession> sessions = [tie, unscheduled, incomplete, early];
        var tieItem = Item(parent, "Tie agenda", 21, 7, 0, -5);
        var unlabelled = Item(parent, "Unlabelled day", 22, 7, 0, 0);
        var later = Item(parent, "Local later", 21, 12, 0, -90);
        List<EventAgendaItem> items = [later, unlabelled, tieItem];
        if (reverseInput)
        {
            days.Reverse();
            sessions.Reverse();
            items.Reverse();
        }

        var result = await Handler(parent, days, sessions, items).QueryAsync(new(parent.Id), default);

        await Assert.That(result!.Timezone).IsEqualTo("Europe/Brussels");
        await Assert.That(result.EventId).IsEqualTo(parent.Id);
        await Assert.That(result.EventTitle).IsEqualTo("Projection event");
        await Assert.That(result.Days.Select(day => day.LocalDate).SequenceEqual(new[]
            { new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 21) })).IsTrue();
        await Assert.That(result.Days[0].EventDayId).IsEqualTo(laterDay.Id);
        await Assert.That(result.Days[0].Entries).IsEmpty();
        await Assert.That(result.Days[0].Label).IsEqualTo("Opening day");
        await Assert.That(result.Days[0].Description).IsEqualTo("Published description");
        await Assert.That(result.Days[0].AllowsDayScopeRegistration).IsTrue();
        await Assert.That(result.Days[1].EventDayId).IsNull();
        await Assert.That(result.Days[1].Label).IsNull();
        await Assert.That(result.Days[1].IsPublished).IsTrue();
        await Assert.That(result.Days[1].AllowsDayScopeRegistration).IsFalse();
        await Assert.That(result.Days[1].SortOrder).IsEqualTo(0);
        var entries = result.Days[2].Entries;
        await Assert.That(entries.Select(entry => entry.Title).SequenceEqual(new[]
            { "Local early", "Tie agenda", "Tie session", "Local later" })).IsTrue();
        await Assert.That(entries.Select(entry => entry.EntryType).SequenceEqual(new[]
            { "Session", "AgendaItem", "Session", "AgendaItem" })).IsTrue();
        await Assert.That(entries[0].LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(entries[0].LocalStartMinuteOfDay).IsEqualTo(90);
        await Assert.That(entries[0].LocalEndMinuteOfDay).IsEqualTo(150);
        await Assert.That(entries[0].StartTime).IsEqualTo(new DateTimeOffset(2026, 7, 20, 23, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task EqualDayPriority_UsesDateAndLegacyTimezoneFallback()
    {
        var parent = Parent();
        parent.Timezone = "America/New_York";
        await Assert.That(parent.EventTimeZoneId).IsNull();
        var result = await Handler(parent, [Day(parent, 23, 0), Day(parent, 21, 0)], [], [])
            .QueryAsync(new(parent.Id), default);
        await Assert.That(result!.Timezone).IsEqualTo("America/New_York");
        await Assert.That(result.Days.Select(day => day.LocalDate)
            .SequenceEqual(new[] { new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 23) })).IsTrue();
    }

    // These snapshots isolate only the existing in-memory merge. Real repository eligibility,
    // tenant filters, disclosure and cancellation are exercised by NativeAgendaProjectionHttpTests.
    private static IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?> Handler(Explore.Domain.Event parent,
        List<EventDay> days, List<EventSession> sessions, List<EventAgendaItem> items)
    {
        var events = Substitute.For<IEventRepository>();
        var eventDays = Substitute.For<IEventDayRepository>();
        var eventSessions = Substitute.For<IEventSessionRepository>();
        var agenda = Substitute.For<IEventAgendaItemRepository>();
        events.GetById(parent.Id).Returns(parent);
        events.IsPubliclyEligibleAsync(parent.TenantId, parent.Id, Arg.Any<CancellationToken>()).Returns(true);
        eventDays.GetByEventAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(days);
        eventSessions.GetPublicSessionsByEventAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(sessions);
        agenda.GetPublicByEventAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(items);
        return new GetEventAgendaProjectionRequestHandler(events, eventDays, eventSessions, agenda, Substitute.For<IEventLocationDisclosureService>());
    }

    private static Explore.Domain.Event Parent() => new()
    {
        Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, Actor = null!, Title = "Projection event",
        VisibilityType = null!, EventStatus = null!, EventFormat = null!
    };

    private static EventDay Day(Explore.Domain.Event parent, int day, int order) => new()
    {
        Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
        Tenant = null!, LocalDate = new DateOnly(2026, 7, day), IsPublished = true, SortOrder = order
    };

    private static EventSession Session(Explore.Domain.Event parent, string title, int day, int hour, int minute, int order)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
            Tenant = null!, Title = title, SortOrder = order
        };
        var start = new DateTimeOffset(2026, 7, day, hour, minute, 0, TimeSpan.Zero);
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "Europe/Brussels", new EventScheduleProjectionCalculator());
        return session;
    }

    private static EventAgendaItem Item(Explore.Domain.Event parent, string title, int day, int hour, int minute, int order)
    {
        var item = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
            Tenant = null!, Title = title, SortOrder = order
        };
        var start = new DateTimeOffset(2026, 7, day, hour, minute, 0, TimeSpan.Zero);
        item.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "Europe/Brussels", new EventScheduleProjectionCalculator());
        return item;
    }
}
