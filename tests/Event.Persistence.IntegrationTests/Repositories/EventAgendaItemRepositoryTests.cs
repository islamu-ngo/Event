// ABOUTME: Persistence integration tests for EventAgendaItemRepository verifying CRUD and event-scoped queries.
// ABOUTME: Uses Testcontainers PostgreSQL with real schema via MigrateAsync and Respawn reset.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EventAgendaItemRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EventAgendaItemRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Create_ShouldPersistEventAgendaItem()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventAgendaItemRepository(context);
        var calculator = new EventScheduleProjectionCalculator();

        var item = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Opening Ceremony",
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            SortOrder = 1,
            Tenant = tenant
        };
        item.ReprojectLocalTimes("Europe/Paris", calculator);

        // Act
        var result = await repository.Create(item);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.EventAgendaItems.FindAsync(result.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Title).IsEqualTo("Opening Ceremony");
        await Assert.That(saved.LocalStartDate).IsEqualTo(new DateOnly(2026, 7, 1));
    }

    [Test]
    public async Task GetByEventAsync_ShouldReturnOrderedItems()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventAgendaItemRepository(context);
        var calculator = new EventScheduleProjectionCalculator();

        var item1 = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Third",
            StartTime = new DateTimeOffset(2026, 7, 1, 11, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 11, 30, 0, TimeSpan.Zero),
            SortOrder = 3,
            Tenant = tenant
        };
        item1.ReprojectLocalTimes("UTC", calculator);

        var item2 = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "First",
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        item2.ReprojectLocalTimes("UTC", calculator);

        var item3 = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Second",
            StartTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.Zero),
            SortOrder = 2,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        item3.ReprojectLocalTimes("UTC", calculator);

        context.EventAgendaItems.AddRange(item1, item2, item3);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetByEventAsync(@event.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].Title).IsEqualTo("First");
        await Assert.That(result[1].Title).IsEqualTo("Second");
        await Assert.That(result[2].Title).IsEqualTo("Third");
    }

    [Test]
    public async Task GetByEventAsync_ShouldReturnEmpty_WhenNoItemsExist()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (_, @event) = await SetupEventAsync(context);
        var repository = new EventAgendaItemRepository(context);

        // Act
        var result = await repository.GetByEventAsync(@event.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Create_WithKindId_ShouldPersistLookupReference()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventAgendaItemRepository(context);
        var calculator = new EventScheduleProjectionCalculator();

        var item = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Prayer Break",
            StartTime = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 12, 15, 0, TimeSpan.Zero),
            KindId = 5, // Prayer (seeded by LookupTableSeeder)
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        item.ReprojectLocalTimes("UTC", calculator);

        // Act
        var result = await repository.Create(item);

        // Assert
        using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.EventAgendaItems.FindAsync(result.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.KindId).IsEqualTo(5);
    }

    [Test]
    public async Task Reschedule_ShouldUpdateLocalProjections()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var calculator = new EventScheduleProjectionCalculator();

        var item = new EventAgendaItem
        {
            EventId = @event.Id,
            Event = @event,
            Title = "Rescheduled Item",
            StartTime = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        item.ReprojectLocalTimes("UTC", calculator);
        context.EventAgendaItems.Add(item);
        await context.SaveChangesAsync();

        // Act
        item.Reschedule(
            UtcInstantRange.Create(new DateTimeOffset(2026, 7, 2, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 2, 15, 30, 0, TimeSpan.Zero)),
            "Europe/Paris",
            calculator);
        await context.SaveChangesAsync();

        // Assert
        using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.EventAgendaItems.FindAsync(item.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.LocalStartDate).IsEqualTo(new DateOnly(2026, 7, 2));
        await Assert.That(saved.LocalStartTime).IsEqualTo(new TimeOnly(16, 0)); // UTC+2 in July
    }

    private static async Task<(Tenant tenant, Explore.Domain.Event @event)> SetupEventAsync(ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Test Tenant",
            Slug = "test-tenant-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);

        var user = new User { Pii = new UserPii { Email = $"agenda-test-{Guid.NewGuid():N}@example.com", FirstName = "Test", LastName = "User" } };
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

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Agenda Test Event",
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
}
