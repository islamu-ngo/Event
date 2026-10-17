using System.Text.Json;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

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

    private static Explore.Domain.Event Parent() => new()
    {
        Id = EventId,
        TenantId = TenantId,
        Title = "Festival",
        EventTimeZoneId = "Europe/Brussels",
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

}
