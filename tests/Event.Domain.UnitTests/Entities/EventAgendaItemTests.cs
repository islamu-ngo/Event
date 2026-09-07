namespace Event.Domain.UnitTests.Entities;

using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

public class EventAgendaItemTests
{
    private readonly EventScheduleProjectionCalculator _calculator = new();

    [Test]
    public async Task EventAgendaItem_ImplementsTenantEntityInterface()
    {
        await Assert.That(typeof(EventAgendaItem).GetInterfaces().Contains(typeof(ITenantEntity))).IsTrue();
    }

    [Test]
    public async Task EventAgendaItem_ImplementsAuditableEntityInterface()
    {
        await Assert.That(typeof(EventAgendaItem).GetInterfaces().Contains(typeof(IAuditableEntity))).IsTrue();
    }

    [Test]
    public async Task EventAgendaItem_ImplementsSoftDeletableInterface()
    {
        await Assert.That(typeof(EventAgendaItem).GetInterfaces().Contains(typeof(ISoftDeletable))).IsTrue();
    }

    [Test]
    public async Task EventAgendaItem_ImplementsConcurrencyAwareInterface()
    {
        await Assert.That(typeof(EventAgendaItem).GetInterfaces().Contains(typeof(IConcurrencyAware))).IsTrue();
    }

    [Test]
    public async Task RequiredProperties_AreMarkedAsRequired()
    {
        await Assert.That(IsRequiredProperty<EventAgendaItem>(nameof(EventAgendaItem.Title))).IsTrue();
        await Assert.That(IsRequiredProperty<EventAgendaItem>(nameof(EventAgendaItem.Event))).IsTrue();
        await Assert.That(IsRequiredProperty<EventAgendaItem>(nameof(EventAgendaItem.Tenant))).IsTrue();
    }

    [Test]
    public async Task DefaultValues_WhenCreated_AreExpected()
    {
        var entity = CreateEventAgendaItem();

        await Assert.That(entity.IsDeleted).IsFalse();
        await Assert.That(entity.SortOrder).IsEqualTo(0);
        await Assert.That(entity.Description).IsNull();
        await Assert.That(entity.LocationId).IsNull();
        await Assert.That(entity.Location).IsNull();
        await Assert.That(entity.RoomId).IsNull();
        await Assert.That(entity.Room).IsNull();
        await Assert.That(entity.KindId).IsNull();
        await Assert.That(entity.Kind).IsNull();
        await Assert.That(entity.EventDayId).IsNull();
        await Assert.That(entity.EventDay).IsNull();
    }

    [Test]
    public async Task Reschedule_WithValidTimes_SetsStartEndAndProjectsLocalFields()
    {
        var item = CreateEventAgendaItem();
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        item.Reschedule(UtcInstantRange.Create(start, end), "Europe/Brussels", _calculator);

        await Assert.That(item.StartTime).IsEqualTo(start);
        await Assert.That(item.EndTime).IsEqualTo(end);
        // 10:00 UTC = 12:00 CEST, 12:00 UTC = 14:00 CEST
        await Assert.That(item.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(item.LocalEndDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(item.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(item.LocalEndTime).IsEqualTo(new TimeOnly(14, 0));
        await Assert.That(item.LocalStartMinuteOfDay).IsEqualTo(720);
        await Assert.That(item.LocalEndMinuteOfDay).IsEqualTo(840);
        await Assert.That(item.GetUtcSchedule()).IsEqualTo(UtcInstantRange.Create(start, end));
        await Assert.That(item.GetLocalDateRange()).IsEqualTo(
            LocalDateRange.Create(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 15)));
    }

    [Test]
    public async Task Reschedule_EndEqualsStart_ThrowsArgumentException()
    {
        var item = CreateEventAgendaItem();
        var time = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await Assert.That(() => UtcInstantRange.Create(time, time))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Reschedule_EndBeforeStart_ThrowsArgumentException()
    {
        var item = CreateEventAgendaItem();
        var start = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await Assert.That(() => UtcInstantRange.Create(start, end))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ReprojectLocalTimes_NullCalculator_ThrowsArgumentNullException()
    {
        var item = CreateEventAgendaItem();

        await Assert.That(() => item.ReprojectLocalTimes("Europe/Brussels", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ReprojectLocalTimes_UpdatesCachedFieldsFromCurrentUtcTimes()
    {
        var item = CreateEventAgendaItem();
        item.StartTime = new DateTimeOffset(2026, 6, 15, 3, 0, 0, TimeSpan.Zero);
        item.EndTime = new DateTimeOffset(2026, 6, 15, 5, 0, 0, TimeSpan.Zero);

        item.ReprojectLocalTimes("Asia/Tokyo", _calculator);

        // 03:00 UTC = 12:00 JST, 05:00 UTC = 14:00 JST
        await Assert.That(item.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(item.LocalEndTime).IsEqualTo(new TimeOnly(14, 0));
        await Assert.That(item.LocalStartMinuteOfDay).IsEqualTo(720);
        await Assert.That(item.LocalEndMinuteOfDay).IsEqualTo(840);
    }

    [Test]
    public async Task ReprojectLocalTimes_OverwritesPreviousProjection()
    {
        var item = CreateEventAgendaItem();
        item.StartTime = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        item.EndTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        item.ReprojectLocalTimes("Europe/Brussels", _calculator);
        await Assert.That(item.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));

        item.ReprojectLocalTimes("America/New_York", _calculator);
        // 10:00 UTC = 06:00 EDT
        await Assert.That(item.LocalStartTime).IsEqualTo(new TimeOnly(6, 0));
    }

    [Test]
    public async Task Reschedule_PreservesNonSchedulingProperties()
    {
        var item = CreateEventAgendaItem();
        item.Description = "Break time";
        item.SortOrder = 3;

        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero);
        item.Reschedule(UtcInstantRange.Create(start, end), "UTC", _calculator);

        await Assert.That(item.Title).IsEqualTo("Agenda Item");
        await Assert.That(item.Description).IsEqualTo("Break time");
        await Assert.That(item.SortOrder).IsEqualTo(3);
    }

    private static bool IsRequiredProperty<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        return property is not null && property.GetCustomAttributes(inherit: false).Any(a => a.GetType().Name == "RequiredMemberAttribute");
    }

    private static EventAgendaItem CreateEventAgendaItem()
    {
        return new EventAgendaItem
        {
            Title = "Agenda Item",
            Event = CreateEvent(),
            Tenant = CreateTenant()
        };
    }

    private static global::Explore.Domain.Event CreateEvent()
    {
        return new global::Explore.Domain.Event
        {
            Title = "Event",
            Actor = new Actor
            {
                Pii = new ActorPii { DisplayName = "Actor" },
                ActorType = new ActorType { FullName = "User", MasterCode = "USER" }
            },
            Tenant = CreateTenant(),
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "DRAFT", FullName = "Draft" },
            EventFormat = new EventFormat { MasterCode = "ONLINE", FullName = "Online" }
        };
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            FullName = "Tenant",
            Slug = "tenant",
            TenantStatusId = 2,
            TenantStatus = new TenantStatus { Id = 2, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true }
        };
    }
}
