// ABOUTME: SQLite portability regression tests for EventSessionRepository query translation boundaries.
// ABOUTME: Proves schedule ordering and prefixed-table move mutations remain portable on SQLite.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventSessionRepositorySqliteTests
{
    [Test]
    public async Task GetSessionsByEvent_OnSqlite_ReturnsStartTimeAscending()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        DomainEvent @event = await SeedEventAsync(context, tenantId);
        DateTimeOffset later = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset earlier = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        context.EventSessions.AddRange(
            CreateSession(@event, "Later", later),
            CreateSession(@event, "Earlier", earlier));
        await context.SaveChangesAsync();

        var repository = new EventSessionRepository(context);
        List<EventSession> sessions = await repository.GetSessionsByEvent(@event.Id);

        await Assert.That(sessions).Count().IsEqualTo(2);
        await Assert.That(sessions[0].StartTime).IsEqualTo(earlier);
        await Assert.That(sessions[1].StartTime).IsEqualTo(later);
    }

    [Test]
    public async Task MoveToEventAsync_OnPrefixedSqlite_UsesMappedSessionTable()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        MoveGraph graph = await SeedMoveGraphAsync(context, tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await new EventSessionRepository(context).MoveToEventAsync(
            graph.Session,
            graph.TargetEvent.Id,
            graph.TargetPlacement,
            roomId: null,
            CancellationToken.None);
        await transaction.CommitAsync();

        context.ChangeTracker.Clear();
        EventSession moved = await context.EventSessions.SingleAsync(item => item.Id == graph.Session.Id);
        await Assert.That(moved.EventId).IsEqualTo(graph.TargetEvent.Id);
        await Assert.That(moved.EventLocationId).IsEqualTo(graph.TargetPlacement.Id);
        await Assert.That(moved.LocationId).IsEqualTo(graph.Location.Id);
        await Assert.That(moved.EventDayId).IsNull();
    }

    [Test]
    public async Task MoveToEventAsync_OnPrefixedSqlite_UsesMappedAgendaTable()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        MoveGraph graph = await SeedMoveGraphAsync(context, tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await new EventAgendaItemRepository(context).MoveToEventAsync(
            graph.AgendaItem,
            graph.TargetEvent.Id,
            graph.TargetPlacement,
            roomId: null,
            CancellationToken.None);
        await transaction.CommitAsync();

        context.ChangeTracker.Clear();
        EventAgendaItem moved = await context.EventAgendaItems.SingleAsync(item => item.Id == graph.AgendaItem.Id);
        await Assert.That(moved.EventId).IsEqualTo(graph.TargetEvent.Id);
        await Assert.That(moved.EventLocationId).IsEqualTo(graph.TargetPlacement.Id);
        await Assert.That(moved.LocationId).IsEqualTo(graph.Location.Id);
        await Assert.That(moved.EventDayId).IsNull();
    }

    [Test]
    public async Task MoveSessionToEvent_WhenAmbientTenantDiffers_RejectsTheMove()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext ownerContext = CreateContext(connection, tenantId);
        await ownerContext.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(ownerContext);
        MoveGraph graph = await SeedMoveGraphAsync(ownerContext, tenantId);
        ownerContext.ChangeTracker.Clear();

        await using ExploreDbContext foreignContext = CreateContext(connection, Guid.CreateVersion7());
        await using var transaction = await foreignContext.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EventSessionRepository(foreignContext).MoveToEventAsync(
                graph.Session,
                graph.TargetEvent.Id,
                graph.TargetPlacement,
                roomId: null,
                CancellationToken.None));

        await transaction.RollbackAsync();
        EventSession unchanged = await ownerContext.EventSessions.SingleAsync(item => item.Id == graph.Session.Id);
        await Assert.That(unchanged.EventId).IsNotEqualTo(graph.TargetEvent.Id);
        await Assert.That(unchanged.EventLocationId).IsNotEqualTo(graph.TargetPlacement.Id);
    }

    [Test]
    public async Task MoveAgendaItemToEvent_WhenAmbientTenantDiffers_RejectsTheMove()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext ownerContext = CreateContext(connection, tenantId);
        await ownerContext.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(ownerContext);
        MoveGraph graph = await SeedMoveGraphAsync(ownerContext, tenantId);
        ownerContext.ChangeTracker.Clear();

        await using ExploreDbContext foreignContext = CreateContext(connection, Guid.CreateVersion7());
        await using var transaction = await foreignContext.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EventAgendaItemRepository(foreignContext).MoveToEventAsync(
                graph.AgendaItem,
                graph.TargetEvent.Id,
                graph.TargetPlacement,
                roomId: null,
                CancellationToken.None));

        await transaction.RollbackAsync();
        EventAgendaItem unchanged = await ownerContext.EventAgendaItems.SingleAsync(item => item.Id == graph.AgendaItem.Id);
        await Assert.That(unchanged.EventId).IsNotEqualTo(graph.TargetEvent.Id);
        await Assert.That(unchanged.EventLocationId).IsNotEqualTo(graph.TargetPlacement.Id);
    }

    [Test]
    public async Task MoveSessionToEvent_WhenSoftDeleted_RejectsTheMove()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        MoveGraph graph = await SeedMoveGraphAsync(context, tenantId);
        await context.EventSessions
            .Where(item => item.Id == graph.Session.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDeleted, true));
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EventSessionRepository(context).MoveToEventAsync(
                graph.Session,
                graph.TargetEvent.Id,
                graph.TargetPlacement,
                roomId: null,
                CancellationToken.None));

        await transaction.RollbackAsync();
        EventSession unchanged = await context.EventSessions
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.Session.Id);
        await Assert.That(unchanged.EventId).IsNotEqualTo(graph.TargetEvent.Id);
        await Assert.That(unchanged.IsDeleted).IsTrue();
    }

    [Test]
    public async Task MoveAgendaItemToEvent_WhenSoftDeleted_RejectsTheMove()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(connection, tenantId);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        MoveGraph graph = await SeedMoveGraphAsync(context, tenantId);
        await context.EventAgendaItems
            .Where(item => item.Id == graph.AgendaItem.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDeleted, true));
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EventAgendaItemRepository(context).MoveToEventAsync(
                graph.AgendaItem,
                graph.TargetEvent.Id,
                graph.TargetPlacement,
                roomId: null,
                CancellationToken.None));

        await transaction.RollbackAsync();
        EventAgendaItem unchanged = await context.EventAgendaItems
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.AgendaItem.Id);
        await Assert.That(unchanged.EventId).IsNotEqualTo(graph.TargetEvent.Id);
        await Assert.That(unchanged.IsDeleted).IsTrue();
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid tenantId) =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options)
        {
            TenantContext = new FixedTenantContext(tenantId),
            CurrentUserService = new FixedCurrentUser()
        };

    private static async Task<DomainEvent> SeedEventAsync(ExploreDbContext context, Guid tenantId)
    {
        var tenant = new Tenant { Id = tenantId, FullName = "Session SQLite Tenant", Slug = $"session-sqlite-{Guid.NewGuid():N}", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! };
        var user = new User { Id = Guid.CreateVersion7(), Pii = new UserPii { Email = $"session-sqlite-{Guid.NewGuid():N}@example.test", FirstName = "Session", LastName = "Owner" } };
        var actor = new Actor { Id = Guid.CreateVersion7(), Pii = new ActorPii { DisplayName = "Session SQLite Actor" }, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, UserId = user.Id };
        var @event = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            Title = "Session SQLite Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(tenant, user, actor, @event);
        await context.SaveChangesAsync();
        return @event;
    }

    private static async Task<MoveGraph> SeedMoveGraphAsync(ExploreDbContext context, Guid tenantId)
    {
        DomainEvent sourceEvent = await SeedEventAsync(context, tenantId);
        Guid actorUserId = await context.Users.Select(item => item.Id).SingleAsync();
        var targetEvent = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            Title = "Target Session SQLite Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = sourceEvent.ActorId,
            Actor = null!,
            OrganizerActorId = sourceEvent.OrganizerActorId,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FullName = "Prefixed SQLite Venue",
            Country = "BE",
            City = "Brussels"
        };
        var createdAtUtc = new DateTime(2026, 8, 16, 8, 0, 0, DateTimeKind.Utc);
        EventLocation sourcePlacement = EventLocation.CreatePhysical(
            tenantId,
            sourceEvent.Id,
            location.Id,
            actorUserId,
            createdAtUtc);
        EventLocation targetPlacement = EventLocation.CreatePhysical(
            tenantId,
            targetEvent.Id,
            location.Id,
            actorUserId,
            createdAtUtc);
        EventSession session = CreateSession(
            sourceEvent,
            "Session move uses mapped table",
            new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));
        session.AssignEventLocation(sourcePlacement);
        var agendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = sourceEvent.Id,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Agenda move uses mapped table",
            StartTime = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.Zero),
            SortOrder = 1
        };
        agendaItem.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        agendaItem.AssignEventLocation(sourcePlacement);
        context.AddRange(targetEvent, location, sourcePlacement, targetPlacement, session, agendaItem);
        await context.SaveChangesAsync();
        return new MoveGraph(targetEvent, location, targetPlacement, session, agendaItem);
    }

    private static EventSession CreateSession(DomainEvent @event, string title, DateTimeOffset startTime) => new(EventSessionStatusEnum.Published)
    {
        Id = Guid.CreateVersion7(),
        EventId = @event.Id,
        Event = null!,
        TenantId = @event.TenantId,
        Tenant = null!,
        Title = title,
        StartTime = startTime,
        EndTime = startTime.AddHours(1),
        RegistrationModeId = (int)RegistrationModeEnum.Open,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }

    private sealed record MoveGraph(
        DomainEvent TargetEvent,
        Location Location,
        EventLocation TargetPlacement,
        EventSession Session,
        EventAgendaItem AgendaItem);
}
