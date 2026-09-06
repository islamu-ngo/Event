// ABOUTME: Persistence integration tests for EventRepository CRUD and aggregate loading.
// ABOUTME: Seeds required tenant, actor, and lookup relationships against PostgreSQL.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EventRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EventRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Create_ShouldPersistEvent()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var repository = new EventRepository(context);

        // Setup dependent data
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Test Tenant",
            Slug = "test-tenant-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);

        var user = new User { Pii = new UserPii { Email = "test@example.com", FirstName = "Test", LastName = "User" } };
        context.Users.Add(user);

        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Test Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);

        await context.SaveChangesAsync();

        var eventType = await SeedConferenceEventTypeAsync(context);

        var eventId = Guid.NewGuid();
        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Integration Test Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Subtitle = "Integration Test Subtitle",
            Description = "Test Description",
            FirstSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1).AddHours(2)),
            EventTypeId = eventType.Id,
            EventType = eventType,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0
        };

        // Act
        var result = await repository.Create(@event);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(eventId);
        await Assert.That(result.OrganizerActor).IsNotNull();
        await Assert.That(result.OrganizerActor!.Id).IsEqualTo(actor.Id);

        // Verify with new context
        using var verifyContext = _fixture.CreateDbContext();
        var savedEvent = await verifyContext.Events.FindAsync(eventId);
        await Assert.That(savedEvent).IsNotNull();
        await Assert.That(savedEvent!.Title).IsEqualTo("Integration Test Event");
    }

    [Test]
    public async Task GetEventWithDetails_ShouldReturnIncludes()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var repository = new EventRepository(context);

        // Setup dependent data
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Test Tenant",
            Slug = "test-tenant-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);

        var user = new User { Pii = new UserPii { Email = "test2@example.com", FirstName = "Test", LastName = "User" } };
        context.Users.Add(user);

        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Test Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);

        await context.SaveChangesAsync();

        var eventType = await SeedConferenceEventTypeAsync(context);

        var eventId = Guid.NewGuid();
        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Detailed Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventTypeId = eventType.Id,
            EventType = eventType,
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

        await repository.Create(@event);

        // Act
        var result = await repository.GetEventWithDetails(eventId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    private static async Task<EventType> SeedConferenceEventTypeAsync(ExploreDbContext context)
    {
        var eventType = await context.EventTypes.FindAsync((int)EventTypeEnum.Conference);
        if (eventType is not null)
            return eventType;

        eventType = new EventType
        {
            Id = (int)EventTypeEnum.Conference,
            MasterCode = "CONFERENCE",
            FullName = "Conference"
        };
        context.EventTypes.Add(eventType);
        await context.SaveChangesAsync();
        return eventType;
    }
}
