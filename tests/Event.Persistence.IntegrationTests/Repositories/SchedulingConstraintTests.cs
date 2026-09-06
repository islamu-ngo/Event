// ABOUTME: Persistence integration tests for DB-enforced constraints on scheduling entities.
// ABOUTME: Covers unique, check, and exclusion constraints for event days, rooms, sessions, and aspects.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class SchedulingConstraintTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public SchedulingConstraintTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task EventDay_ShouldRejectDuplicateLocalDate_ForSameEvent()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var calculator = new EventScheduleProjectionCalculator();

        var day1 = new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "First Day",
            SortOrder = 1,
            Tenant = tenant
        };
        context.EventDays.Add(day1);
        await context.SaveChangesAsync();

        using var context2 = _fixture.CreateDbContext();
        var day2 = new EventDay
        {
            EventId = @event.Id,
            Event = null!,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "Duplicate Date",
            SortOrder = 2,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context2.EventDays.Add(day2);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context2.SaveChangesAsync());
    }

    [Test]
    public async Task EventDay_ShouldAllowSameLocalDate_ForDifferentEvents()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var event2 = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Second Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = @event.ActorId,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0
        };
        context.Events.Add(event2);
        await context.SaveChangesAsync();

        var repository = new EventDayRepository(context);

        var day1 = new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "Event 1 Day",
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        await repository.Create(day1);

        var day2 = new EventDay
        {
            EventId = event2.Id,
            Event = event2,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "Event 2 Day",
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };

        var result = await repository.Create(day2);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task LocationRoom_ShouldRejectDuplicateName_ForSameLocation()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, location) = await SetupLocationAsync(context);

        var room1 = new LocationRoom
        {
            LocationId = location.Id,
            Location = null!,
            Name = "Main Hall",
            Capacity = 200,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.LocationRooms.Add(room1);
        await context.SaveChangesAsync();

        using var context2 = _fixture.CreateDbContext();
        var room2 = new LocationRoom
        {
            LocationId = location.Id,
            Location = null!,
            Name = "Main Hall",
            Capacity = 100,
            SortOrder = 2,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context2.LocationRooms.Add(room2);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context2.SaveChangesAsync());
    }

    [Test]
    public async Task LocationRoom_ShouldRejectNegativeCapacity()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, location) = await SetupLocationAsync(context);

        var room = new LocationRoom
        {
            LocationId = location.Id,
            Location = null!,
            Name = "Bad Room",
            Capacity = -5,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.LocationRooms.Add(room);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task LocationRoom_ShouldAllowNullCapacity()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, location) = await SetupLocationAsync(context);
        var repository = new LocationRoomRepository(context);

        var room = new LocationRoom
        {
            LocationId = location.Id,
            Location = location,
            Name = "Open Space",
            Capacity = null,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };

        var result = await repository.Create(room);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task EventAgendaItem_ShouldRejectEndTimeBeforeStartTime()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);

        var item = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = null!,
            Title = "Bad Item",
            StartTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.EventAgendaItems.Add(item);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventAgendaItem_ShouldAcceptValidTimeRange()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var calculator = new EventScheduleProjectionCalculator();
        var repository = new EventAgendaItemRepository(context);

        var item = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Valid Item",
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        item.ReprojectLocalTimes("UTC", calculator);

        var result = await repository.Create(item);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task EventSession_ShouldRejectEndTimeBeforeStartTime()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);

        var session = new EventSession
        {
            EventId = @event.Id,
            Event = null!,
            StartTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.EventSessions.Add(session);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSession_ShouldRejectLocalMinuteThatDoesNotMatchLocalTime()
    {
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);

        var session = new EventSession
        {
            EventId = @event.Id,
            Event = null!,
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            TenantId = tenant.Id,
            Tenant = null!
        };
        session.Reschedule(UtcInstantRange.Create(session.StartTime!.Value, session.EndTime!.Value), "Europe/Brussels", new EventScheduleProjectionCalculator());
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();

        context.Entry(session).Property(nameof(EventSession.LocalStartMinuteOfDay)).CurrentValue = 999;

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSession_ShouldRejectOverlappingSessionsInSameRoom()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);

        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSession_ShouldAllowAdjacentSessionsInSameRoom()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);

        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero)));
        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 11, 0, 0, TimeSpan.Zero)));

        await context.SaveChangesAsync();

        await Assert.That(await context.EventSessions.CountAsync(s => s.RoomId == scope.Room.Id)).IsEqualTo(2);
    }

    [Test]
    public async Task EventSession_ShouldAllowOverlappingSessionsInDifferentRooms()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var secondRoom = new LocationRoom
        {
            LocationId = scope.Location.Id,
            Location = null!,
            Name = "Workshop Room",
            Capacity = 80,
            SortOrder = 2,
            TenantId = scope.Tenant.Id,
            Tenant = null!
        };
        context.LocationRooms.Add(secondRoom);
        await context.SaveChangesAsync();

        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero)));

        var secondScope = scope with { Room = secondRoom };
        context.EventSessions.Add(CreateRoomSession(
            secondScope,
            new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.Zero)));

        await context.SaveChangesAsync();

        await Assert.That(await context.EventSessions.CountAsync(s => s.LocationId == scope.Location.Id)).IsEqualTo(2);
    }

    [Test]
    public async Task EventSession_ShouldAllowOverlapAfterConflictingSessionSoftDeleted()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var first = CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        context.EventSessions.Add(first);
        await context.SaveChangesAsync();

        context.EventSessions.Remove(first);
        await context.SaveChangesAsync();

        context.EventSessions.Add(CreateRoomSession(
            scope,
            new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.Zero)));

        await context.SaveChangesAsync();

        await Assert.That(await context.EventSessions.IncludeDeleted().CountAsync(s => s.RoomId == scope.Room.Id)).IsEqualTo(2);
    }

    [Test]
    public async Task EventSessionIslamicAspect_ShouldRejectFixedScheduleWithPrayerFields()
    {
        using var context = _fixture.CreateDbContext();
        var session = await SetupSessionAsync(context);

        context.EventSessionIslamicAspects.Add(new EventSessionIslamicAspect
        {
            EventSessionId = session.Id,
            StartTimeType = SessionStartTimeType.Fixed,
            ReferencePrayer = PrayerTime.Dhuhr,
            OffsetMinutes = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSessionIslamicAspect_ShouldRejectRelativeScheduleWithoutOffset()
    {
        using var context = _fixture.CreateDbContext();
        var session = await SetupSessionAsync(context);

        context.EventSessionIslamicAspects.Add(new EventSessionIslamicAspect
        {
            EventSessionId = session.Id,
            StartTimeType = SessionStartTimeType.RelativeToPrayer,
            ReferencePrayer = PrayerTime.Asr,
            OffsetMinutes = null
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSessionIslamicAspect_ShouldRejectOffsetOutsideEnterpriseRange()
    {
        using var context = _fixture.CreateDbContext();
        var session = await SetupSessionAsync(context);

        context.EventSessionIslamicAspects.Add(new EventSessionIslamicAspect
        {
            EventSessionId = session.Id,
            StartTimeType = SessionStartTimeType.RelativeToPrayer,
            ReferencePrayer = PrayerTime.Maghrib,
            OffsetMinutes = 181
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSessionIslamicAspect_ShouldAcceptExactRelativeScheduleState()
    {
        using var context = _fixture.CreateDbContext();
        var session = await SetupSessionAsync(context);

        var aspect = new EventSessionIslamicAspect
        {
            EventSessionId = session.Id,
            RequiresWudu = true
        };
        aspect.ApplyScheduling(SessionStartTimeType.RelativeToPrayer, PrayerTime.Dhuhr, -30);
        context.EventSessionIslamicAspects.Add(aspect);

        await context.SaveChangesAsync();

        await Assert.That(aspect.ReferencePrayer).IsEqualTo(PrayerTime.Dhuhr);
        await Assert.That(aspect.OffsetMinutes).IsEqualTo(-30);
    }

    private static async Task<(Tenant tenant, Explore.Domain.Event @event)> SetupEventAsync(ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Constraint Test Tenant",
            Slug = "constraint-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);

        var user = new User { Pii = new UserPii { Email = $"constraint-{Guid.NewGuid():N}@example.com", FirstName = "Test", LastName = "User" } };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Constraint Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Constraint Test Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return (tenant, @event);
    }

    private static async Task<RoomScheduleScope> SetupRoomScopeAsync(ExploreDbContext context)
    {
        var (tenant, @event) = await SetupEventAsync(context);
        var actorUserId = await context.Actors
            .Where(actor => actor.Id == @event.ActorId)
            .Select(actor => actor.UserId)
            .SingleAsync()
            ?? throw new InvalidOperationException("The room-schedule fixture actor must have a user identity.");
        var location = new Location
        {
            FullName = "Constraint Test Venue",
            Country = "BE",
            City = "Brussels",
            TenantId = tenant.Id,
            Tenant = null!
        };
        location.SetManualAddress("123 Test St", "1000");
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var room = new LocationRoom
        {
            LocationId = location.Id,
            Location = null!,
            Name = "Main Hall",
            Capacity = 200,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.LocationRooms.Add(room);
        await context.SaveChangesAsync();

        var eventLocation = EventLocation.CreatePhysical(
            tenant.Id,
            @event.Id,
            location.Id,
            actorUserId,
            DateTime.UtcNow);
        context.Set<EventLocation>().Add(eventLocation);
        await context.SaveChangesAsync();

        return new RoomScheduleScope(tenant, @event, location, room, eventLocation);
    }

    private static EventSession CreateRoomSession(
        RoomScheduleScope scope,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var session = new EventSession
        {
            EventId = scope.Event.Id,
            Event = null!,
            StartTime = startUtc,
            EndTime = endUtc,
            TenantId = scope.Tenant.Id,
            Tenant = null!
        };
        session.AssignEventLocation(scope.EventLocation);
        session.RoomId = scope.Room.Id;
        session.Reschedule(UtcInstantRange.Create(startUtc, endUtc), "UTC", new EventScheduleProjectionCalculator());

        return session;
    }

    private static async Task<EventSession> SetupSessionAsync(ExploreDbContext context)
    {
        var (tenant, @event) = await SetupEventAsync(context);
        var session = new EventSession
        {
            EventId = @event.Id,
            Event = null!,
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            TenantId = tenant.Id,
            Tenant = null!
        };
        session.Reschedule(UtcInstantRange.Create(session.StartTime!.Value, session.EndTime!.Value), "UTC", new EventScheduleProjectionCalculator());
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    private static async Task<(Tenant tenant, Location location)> SetupLocationAsync(ExploreDbContext context)
    {
        var (tenant, @event) = await SetupEventAsync(context);

        var location = new Location
        {
            FullName = "Constraint Test Venue",
            Country = "BE",
            City = "Brussels",
            TenantId = tenant.Id,
            Tenant = tenant
        };
        location.SetManualAddress("123 Test St", "1000");
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        return (tenant, location);
    }

    private sealed record RoomScheduleScope(
        Tenant Tenant,
        Explore.Domain.Event Event,
        Location Location,
        LocationRoom Room,
        EventLocation EventLocation);
}
