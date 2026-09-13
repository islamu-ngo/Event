using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Handlers.Commands;
using Explore.Application.Features.EventDays.Handlers.Queries;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventDayMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid DayId = Guid.Parse("01900000-0000-7000-8000-000000000003");

    [Test]
    public async Task DayProjections_PreserveScheduleRegistrationAndBoundedJson()
    {
        var parent = Parent();
        var day = new EventDay { Id = DayId, EventId = EventId, Event = parent, TenantId = TenantId, Tenant = null!, LocalDate = new DateOnly(2026, 7, 20), Label = "Day one", Description = "Description", BannerText = "Welcome", IsPublished = true, SortOrder = 4, AllowsDayScopeRegistration = true, ConcurrencyStamp = DayId };
        parent.Days.Add(day);
        var detail = EventMapper.ToDetail(day);
        var list = EventMapper.ToListItem(day);
        await Assert.That(detail.EventTitle).IsEqualTo("Festival");
        await Assert.That(detail.LocalDate).IsEqualTo(new DateOnly(2026, 7, 20));
        await Assert.That(detail.BannerText).IsEqualTo("Welcome");
        await Assert.That(detail.TenantId).IsEqualTo(TenantId);
        await Assert.That(list.AllowsDayScopeRegistration).IsTrue();
        await Assert.That(list.SortOrder).IsEqualTo(4);
        await Assert.That(list.ConcurrencyStamp).IsEqualTo(DayId);
        day.Label = "Changed";
        await Assert.That(list.Label).IsEqualTo("Day one");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(list));
        await Assert.That(json.RootElement.GetProperty("LocalDate").GetString()).IsEqualTo("2026-07-20");
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(8);
        await Assert.That(json.RootElement.TryGetProperty("Event", out _)).IsFalse();
    }

    [Test]
    public async Task CreateDay_PreservesAllowlistAndUsesParentTenant()
    {
        var events = Substitute.For<IEventRepository>();
        var days = Substitute.For<IEventDayRepository>();
        events.Exists(EventId).Returns(true);
        events.GetById(EventId).Returns(Parent());
        EventDay? saved = null;
        days.Create(Arg.Any<EventDay>()).Returns(call => { var entity = call.Arg<EventDay>(); saved = entity; return entity; });
        await new CreateEventDayCommandHandler(days, events, Substitute.For<IStorageObjectRepository>()).ExecuteAsync(new CreateEventDayCommand { EventDayDto = new CreateEventDayDto { EventId = EventId, LocalDate = new DateOnly(2026, 7, 20), Label = "Day one", Description = "", BannerText = "Welcome", IsPublished = true, SortOrder = 4, AllowsDayScopeRegistration = true } }, CancellationToken.None);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.TenantId).IsEqualTo(TenantId);
        await Assert.That(saved.EventId).IsEqualTo(EventId);
        await Assert.That(saved.LocalDate).IsEqualTo(new DateOnly(2026, 7, 20));
        await Assert.That(saved.Description).IsEqualTo("");
        await Assert.That(saved.IsPublished).IsTrue();
        await Assert.That(saved.AllowsDayScopeRegistration).IsTrue();
        await Assert.That(saved.SortOrder).IsEqualTo(4);
        await Assert.That(saved.Id).IsEqualTo(Guid.Empty);
        await Assert.That(saved.CreatedAt).IsEqualTo(default(DateTime));
        await Assert.That(saved.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(saved.Event).IsNull();
    }

    [Test]
    public async Task PublicDayQueries_RespectEligibilityAndManagedListPreservesOrder()
    {
        var events = Substitute.For<IEventRepository>();
        var days = Substitute.For<IEventDayRepository>();
        var day = new EventDay { Id = DayId, EventId = EventId, TenantId = TenantId, Event = null!, Tenant = null!, Label = "First" };
        events.GetById(EventId).Returns(Parent());
        days.GetById(DayId).Returns(day);
        days.GetByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns([day]);
        var detail = new GetEventDayDetailRequestHandler(events, days);
        var list = new GetEventDaysByEventRequestHandler(events, days);
        await Assert.That(await detail.QueryAsync(new GetEventDayDetailRequest(DayId), CancellationToken.None)).IsNull();
        await Assert.That(await list.QueryAsync(new GetEventDaysByEventRequest(EventId), CancellationToken.None)).IsEmpty();
        events.IsPubliclyEligibleAsync(TenantId, EventId, Arg.Any<CancellationToken>()).Returns(true);
        await Assert.That((await detail.QueryAsync(new GetEventDayDetailRequest(DayId), CancellationToken.None))!.Label).IsEqualTo("First");
        var managed = await new GetManagedEventDaysByEventRequestHandler(days).QueryAsync(new GetManagedEventDaysByEventRequest { EventId = EventId }, CancellationToken.None);
        await Assert.That(managed[0].Id).IsEqualTo(DayId);
    }

    private static Explore.Domain.Event Parent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Festival", Actor = null!, Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!
    };
}
