using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Handlers.Commands;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Mappings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

[Category("EventAgendaMapping")]
public sealed class EventAgendaMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid LocationId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid DayId = Guid.Parse("01900000-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 23, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task AgendaProjection_PreservesDomainScheduleAndNeverDisclosesPlacementGraph()
    {
        var parent = Parent();
        var item = new EventAgendaItem { Id = DayId, EventId = EventId, TenantId = TenantId, Event = parent, Tenant = null!, Title = "Opening", LocationId = LocationId, RoomId = DayId, SortOrder = 7, ConcurrencyStamp = DayId, Kind = new ScheduleItemKind { FullName = "Opening", MasterCode = "OPENING" } };
        parent.AgendaItems.Add(item);
        item.Reschedule(UtcInstantRange.Create(Start, Start.AddHours(1)), "Europe/Brussels", new EventScheduleProjectionCalculator());
        var dto = EventMapper.ToDetail(item);
        var list = EventMapper.ToListItem(item);
        await Assert.That(dto.LocationId).IsNull();
        await Assert.That(dto.RoomId).IsNull();
        await Assert.That(dto.EventLocation).IsNull();
        await Assert.That(dto.EventTitle).IsEqualTo("Festival");
        await Assert.That(dto.LocalStartDate).IsEqualTo(new DateOnly(2026, 7, 21));
        await Assert.That(dto.LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(dto.LocalEndTime).IsEqualTo(new TimeOnly(2, 30));
        await Assert.That(dto.LocalStartMinuteOfDay).IsEqualTo(90);
        await Assert.That(list.KindFullName).IsEqualTo("Opening");
        await Assert.That(list.StartTime).IsEqualTo(Start);
        await Assert.That(list.ConcurrencyStamp).IsEqualTo(DayId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(list));
        await Assert.That(json.RootElement.TryGetProperty("LocationId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Event", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("LocalStartDate").GetString()).IsEqualTo("2026-07-21");
    }

    [Test]
    public async Task CreateAgenda_UsesDomainSchedulingParentTenantAndTransactionOwnedPlacement()
    {
        var events = Substitute.For<IEventRepository>();
        var days = Substitute.For<IEventDayRepository>();
        var items = Substitute.For<IEventAgendaItemRepository>();
        events.Exists(EventId).Returns(true);
        events.GetById(EventId).Returns(Parent());
        days.FindByEventAndLocalDateAsync(EventId, new DateOnly(2026, 7, 21), Arg.Any<CancellationToken>()).Returns(new EventDay { Id = DayId, Event = null!, Tenant = null! });
        var locations = Substitute.For<IEventLocationRepository>();
        var placement = EventLocation.CreatePhysical(TenantId, EventId, LocationId, DayId, Start.UtcDateTime);
        locations.FindActivePhysicalAsync(EventId, LocationId, Arg.Any<CancellationToken>()).Returns(placement);
        locations.GetForUpdateAsync(placement.Id, Arg.Any<CancellationToken>()).Returns(placement);
        var transaction = new InlineUnitOfWork();
        EventAgendaItem? saved = null;
        items.Create(Arg.Any<EventAgendaItem>()).Returns(call =>
        {
            if (!transaction.IsInside) throw new InvalidOperationException("Agenda persistence must remain inside the handler transaction.");
            EventAgendaItem entity = call.Arg<EventAgendaItem>() ?? throw new InvalidOperationException("Expected agenda entity at persistence boundary.");
            saved = entity;
            return entity;
        });
        var attachment = new EventLocationAttachmentService(locations, Substitute.For<IUserContext>(), Substitute.For<ITenantContext>(), TimeProvider.System);
        var result = await new CreateEventAgendaItemCommandHandler(items, events, days, new EventScheduleProjectionCalculator(), transaction, attachment).Handle(new CreateEventAgendaItemCommand { EventAgendaItemDto = new CreateEventAgendaItemDto { EventId = EventId, Title = "Opening", Description = "", StartTime = Start, EndTime = Start.AddHours(1), LocationId = LocationId, RoomId = DayId, KindId = 7, SortOrder = 4 } }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.TenantId).IsEqualTo(TenantId);
        await Assert.That(saved.EventLocationId).IsEqualTo(placement.Id);
        await Assert.That(saved.EventDayId).IsEqualTo(DayId);
        await Assert.That(saved.RoomId).IsEqualTo(DayId);
        await Assert.That(saved.LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(saved.LocalEndTime).IsEqualTo(new TimeOnly(2, 30));
        await Assert.That(saved.KindId).IsEqualTo(7);
        await Assert.That(saved.SortOrder).IsEqualTo(4);
        await Assert.That(saved.Description).IsEqualTo("");
        await Assert.That(saved.CreatedAt).IsEqualTo(default(DateTime));
        await Assert.That(saved.ConcurrencyStamp).IsEqualTo(Guid.Empty);
    }

    private static Explore.Domain.Event Parent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Festival", EventTimeZoneId = "Europe/Brussels", Actor = null!, Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!
    };

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public bool IsInside { get; private set; }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            IsInside = true;
            try { return await operation(ct); }
            finally { IsInside = false; }
        }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }
}
