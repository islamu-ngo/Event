// ABOUTME: Integration tests for EventSessionStatus lookup and nullable EventSession schedule constraints.
// ABOUTME: Verifies Phase 1 persistence foundation: seeded statuses, Restrict FK, nullable schedule, partial GiST exclusion.
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
public class EventSessionLifecycleConstraintTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EventSessionLifecycleConstraintTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task EventSessionStatus_ShouldSeedAllTenRows()
    {
        using var context = _fixture.CreateDbContext();
        var statuses = await context.EventSessionStatuses.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        await Assert.That(statuses.Count).IsEqualTo(10);
        await Assert.That(statuses[0].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Draft));
        await Assert.That(statuses[1].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Submitted));
        await Assert.That(statuses[2].FullName).IsEqualTo("Under review");
        await Assert.That(statuses[3].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Approved));
        await Assert.That(statuses[4].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Published));
        await Assert.That(statuses[5].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Rejected));
        await Assert.That(statuses[6].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Cancelled));
        await Assert.That(statuses[7].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Archived));
        await Assert.That(statuses[8].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Completed));
        await Assert.That(statuses[9].FullName).IsEqualTo(nameof(EventSessionStatusEnum.Moderated));
    }

    [Test]
    public async Task EventSessionStatus_ShouldRejectDeletionWhenReferencedBySession()
    {
        using var setupContext = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(setupContext);
        var session = CreateRoomSession(scope, DateTimeOffset.Parse("2026-07-01T10:00:00Z"), DateTimeOffset.Parse("2026-07-01T11:00:00Z"));
        setupContext.EventSessions.Add(session);
        await setupContext.SaveChangesAsync();

        using var deleteContext = _fixture.CreateDbContext();
        var draftStatus = await deleteContext.EventSessionStatuses.FindAsync((int)EventSessionStatusEnum.Draft);
        deleteContext.EventSessionStatuses.Remove(draftStatus!);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await deleteContext.SaveChangesAsync());
    }

    [Test]
    public async Task EventSession_ShouldPersistWithNullSchedule()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var session = CreateUnscheduledRoomSession(scope);
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = await context.EventSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id);
        await Assert.That(reloaded.StartTime).IsNull();
        await Assert.That(reloaded.EndTime).IsNull();
    }

    [Test]
    public async Task EventSession_ShouldPersistWithNullLocalProjections()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var session = CreateUnscheduledRoomSession(scope);
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = await context.EventSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id);
        await Assert.That(reloaded.LocalStartDate).IsNull();
        await Assert.That(reloaded.LocalEndDate).IsNull();
        await Assert.That(reloaded.LocalStartTime).IsNull();
        await Assert.That(reloaded.LocalEndTime).IsNull();
        await Assert.That(reloaded.LocalStartMinuteOfDay).IsNull();
        await Assert.That(reloaded.LocalEndMinuteOfDay).IsNull();
    }

    [Test]
    public async Task EventSession_CheckConstraints_ShouldAllowNullSchedule()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var session = CreateUnscheduledRoomSession(scope);
        context.EventSessions.Add(session);
        // Verifies CK_EventSession_EndAfterStart, LocalStartMinuteRange, LocalEndMinuteRange,
        // LocalStartMinuteMatchesTime, LocalEndMinuteMatchesTime all accept null schedule.
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task EventSession_GiSTExclusion_ShouldAllowUnscheduledSessionWithRoom()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var scheduled = CreateRoomSession(scope, DateTimeOffset.Parse("2026-07-01T10:00:00Z"), DateTimeOffset.Parse("2026-07-01T11:00:00Z"));
        context.EventSessions.Add(scheduled);
        await context.SaveChangesAsync();

        // Unscheduled session in same room — partial GiST exclusion exempts null-schedule rows.
        var unscheduled = CreateUnscheduledRoomSession(scope);
        context.EventSessions.Add(unscheduled);
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task EventSession_GiSTExclusion_ShouldRejectOverlappingScheduledSessions()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var first = CreateRoomSession(scope, DateTimeOffset.Parse("2026-07-01T10:00:00Z"), DateTimeOffset.Parse("2026-07-01T11:00:00Z"));
        context.EventSessions.Add(first);
        await context.SaveChangesAsync();

        var overlapping = CreateRoomSession(scope, DateTimeOffset.Parse("2026-07-01T10:30:00Z"), DateTimeOffset.Parse("2026-07-01T11:30:00Z"));
        context.EventSessions.Add(overlapping);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EventSession_ShouldDefaultToDraftStatusWhenNotSet()
    {
        using var context = _fixture.CreateDbContext();
        var scope = await SetupRoomScopeAsync(context);
        var session = CreateUnscheduledRoomSession(scope);
        // EventSessionStatusId not explicitly set — relies on EF/database default of 1 (Draft).
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = await context.EventSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id);
        await Assert.That(reloaded.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Draft);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event, Guid UserId)> SetupEventAsync(ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant { FullName = "Lifecycle Test Tenant", Slug = "lifecycle-" + Guid.NewGuid().ToString("N")[..8], TenantStatusId = activeStatus?.Id ?? 2, TenantStatus = activeStatus! };
        context.Tenants.Add(tenant);
        var user = new User { Pii = new UserPii { Email = $"lifecycle-{Guid.NewGuid():N}@example.com", FirstName = "Test", LastName = "User" } };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Lifecycle Actor" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var @event = new Explore.Domain.Event(EventStatusEnum.Draft) { Id = Guid.NewGuid(), Title = "Lifecycle Test Event", EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated, EventTypeId = 1, AudienceGenderId = 1, AudienceAgeId = 1, ActorId = actor.Id, Actor = null!, TenantId = tenant.Id, Tenant = null!, VisibilityTypeId = 1, VisibilityType = null!, EventStatus = null!, EventFormatId = 1, EventFormat = null!, TotalViews = 0 };
        context.Events.Add(@event);
        await context.SaveChangesAsync();
        return (tenant, @event, user.Id);
    }

    private static async Task<RoomScheduleScope> SetupRoomScopeAsync(ExploreDbContext context)
    {
        var (tenant, @event, userId) = await SetupEventAsync(context);
        var location = new Location { FullName = "Lifecycle Test Venue", Country = "BE", City = "Brussels", TenantId = tenant.Id, Tenant = null! };
        location.SetManualAddress("123 Test St", "1000");
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        var room = new LocationRoom { LocationId = location.Id, Location = null!, Name = "Main Hall", Capacity = 200, SortOrder = 1, TenantId = tenant.Id, Tenant = null! };
        context.LocationRooms.Add(room);
        await context.SaveChangesAsync();
        // ck_event_session_physical_location_requires_event_location: a session may only carry a
        // physical location through the EventLocation aggregate that owns its disclosure policy.
        var eventLocation = EventLocation.CreatePhysical(
            tenant.Id,
            @event.Id,
            location.Id,
            userId,
            DateTime.UtcNow);
        context.Set<EventLocation>().Add(eventLocation);
        await context.SaveChangesAsync();
        return new RoomScheduleScope(tenant, @event, location, room, eventLocation);
    }

    private static EventSession CreateRoomSession(RoomScheduleScope scope, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var session = new EventSession { EventId = scope.Event.Id, Event = null!, StartTime = startUtc, EndTime = endUtc, TenantId = scope.Tenant.Id, Tenant = null! };
        session.AssignEventLocation(scope.EventLocation);
        session.RoomId = scope.Room.Id;
        session.Reschedule(UtcInstantRange.Create(startUtc, endUtc), "UTC", new EventScheduleProjectionCalculator());
        return session;
    }

    private static EventSession CreateUnscheduledRoomSession(RoomScheduleScope scope)
    {
        var session = new EventSession { EventId = scope.Event.Id, Event = null!, StartTime = null, EndTime = null, TenantId = scope.Tenant.Id, Tenant = null! };
        session.AssignEventLocation(scope.EventLocation);
        session.RoomId = scope.Room.Id;
        return session;
    }

    private sealed record RoomScheduleScope(
        Tenant Tenant,
        Explore.Domain.Event Event,
        Location Location,
        LocationRoom Room,
        EventLocation EventLocation);
}
