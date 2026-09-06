// ABOUTME: Persistence integration tests for EventDayRepository verifying CRUD, event-scoped queries, and FindByEventAndLocalDate.
// ABOUTME: Uses Testcontainers PostgreSQL with real schema via MigrateAsync and Respawn reset.

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
public class EventDayRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EventDayRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Create_ShouldPersistEventDay()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        var day = new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "Opening Day",
            IsPublished = true,
            SortOrder = 1,
            Tenant = tenant
        };

        // Act
        var result = await repository.Create(day);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        // Verify with new context
        using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.EventDays.FindAsync(result.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Label).IsEqualTo("Opening Day");
        await Assert.That(saved.LocalDate).IsEqualTo(new DateOnly(2026, 7, 1));
    }

    [Test]
    public async Task GetByEventAsync_ShouldReturnOrderedDays()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        context.EventDays.AddRange(
            new EventDay { EventId = @event.Id, Event = @event, LocalDate = new DateOnly(2026, 7, 3), SortOrder = 3, TenantId = tenant.Id, Tenant = tenant },
            new EventDay { EventId = @event.Id, Event = @event, LocalDate = new DateOnly(2026, 7, 1), SortOrder = 1, TenantId = tenant.Id, Tenant = tenant },
            new EventDay { EventId = @event.Id, Event = @event, LocalDate = new DateOnly(2026, 7, 2), SortOrder = 2, TenantId = tenant.Id, Tenant = tenant }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetByEventAsync(@event.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].SortOrder).IsEqualTo(1);
        await Assert.That(result[1].SortOrder).IsEqualTo(2);
        await Assert.That(result[2].SortOrder).IsEqualTo(3);
    }

    [Test]
    public async Task FindByEventAndLocalDateAsync_ShouldReturnMatchingDay()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        var targetDate = new DateOnly(2026, 8, 15);
        context.EventDays.Add(new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = targetDate,
            Label = "Target Day",
            Tenant = tenant
        });
        await context.SaveChangesAsync();

        // Act
        var result = await repository.FindByEventAndLocalDateAsync(@event.Id, targetDate, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Label).IsEqualTo("Target Day");
    }

    [Test]
    public async Task FindByEventAndLocalDateAsync_ShouldReturnNull_WhenNoMatch()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (_, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        // Act
        var result = await repository.FindByEventAndLocalDateAsync(@event.Id, new DateOnly(2099, 1, 1), CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task BelongsToEventAsync_ShouldReturnTrue_WhenDayBelongsToEvent()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        var day = new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = new DateOnly(2026, 9, 1),
            TenantId = tenant.Id,
            Tenant = tenant
        };
        context.EventDays.Add(day);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.BelongsToEventAsync(day.Id, @event.Id, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task BelongsToEventAsync_ShouldReturnFalse_WhenDayDoesNotBelongToEvent()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, @event) = await SetupEventAsync(context);
        var repository = new EventDayRepository(context);

        var day = new EventDay
        {
            EventId = @event.Id,
            Event = @event,
            LocalDate = new DateOnly(2026, 9, 2),
            TenantId = tenant.Id,
            Tenant = tenant
        };
        context.EventDays.Add(day);
        await context.SaveChangesAsync();

        // Act - check against a random event ID
        var result = await repository.BelongsToEventAsync(day.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
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

        var user = new User { Pii = new UserPii { Email = $"day-test-{Guid.NewGuid():N}@example.com", FirstName = "Test", LastName = "User" } };
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
            Title = "Day Test Event",
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
