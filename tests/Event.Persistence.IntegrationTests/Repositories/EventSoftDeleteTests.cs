using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>
/// Tests that the Event entity soft-delete behavior works correctly on real PostgreSQL.
/// Verifies that the SoftDelete query filter hides deleted events from normal queries
/// while IgnoreQueryFilters still finds them with IsDeleted=true.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EventSoftDeleteTests(PostgreSqlContainerFixture fixture)
{
    private readonly PostgreSqlContainerFixture _fixture = fixture;

    [Test]
    public async Task Delete_ShouldSoftDelete_AndExcludeFromNormalQueries()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, _, eventId) = await SeedEventWithDependencies(context, "Soft Delete Event");

        // Act — Remove triggers ISoftDeletable interceptor in SaveChangesAsync
        var eventToDelete = await context.Events.FindAsync(eventId);
        context.Remove(eventToDelete!);
        await context.SaveChangesAsync();

        // Assert — normal query excludes soft-deleted event
        using var verifyContext = _fixture.CreateDbContext();
        var normalResult = await verifyContext.Events
            .Where(e => e.Id == eventId)
            .FirstOrDefaultAsync();
        await Assert.That(normalResult).IsNull();

        // Assert — IgnoreQueryFilters finds it with IsDeleted=true
        var unfilteredResult = await verifyContext.Events
            .IgnoreQueryFilters()
            .Where(e => e.Id == eventId)
            .FirstOrDefaultAsync();
        await Assert.That(unfilteredResult).IsNotNull();
        await Assert.That(unfilteredResult!.IsDeleted).IsTrue();
        await Assert.That(unfilteredResult.DeletedAt).IsNotNull();
    }

    [Test]
    public async Task NonDeletedEvent_ShouldBeVisibleInNormalQuery()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (_, _, eventId) = await SeedEventWithDependencies(context, "Visible Event");

        // Assert — non-deleted event visible in normal query
        using var verifyContext = _fixture.CreateDbContext();
        var result = await verifyContext.Events
            .Where(e => e.Id == eventId)
            .FirstOrDefaultAsync();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsDeleted).IsFalse();
    }

    [Test]
    public async Task SoftDeletedEvent_ShouldNotAppearInScopedCount()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId, eventId) = await SeedEventWithDependencies(context, "Counted Event");

        // Seed another non-deleted event in the same tenant
        var survivingEvent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Surviving Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actorId,
            Actor = null!,
            TenantId = tenantId,
            Tenant = null!,
            EventStatus = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.Events.Add(survivingEvent);
        await context.SaveChangesAsync();

        // Soft-delete the first event
        var eventToDelete = await context.Events.FindAsync(eventId);
        context.Remove(eventToDelete!);
        await context.SaveChangesAsync();

        // Assert — scoped count excludes soft-deleted event
        using var verifyContext = _fixture.CreateDbContext();
        var visibleCount = await verifyContext.Events
            .Where(e => e.TenantId == tenantId)
            .CountAsync();
        await Assert.That(visibleCount).IsEqualTo(1);

        // Assert — unfiltered count includes both (scoped to tenant for test isolation)
        var totalCount = await verifyContext.Events
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .CountAsync();
        await Assert.That(totalCount).IsEqualTo(2);
    }

    #region Helpers

    private static async Task<(Guid TenantId, Guid ActorId, Guid EventId)> SeedEventWithDependencies(
        ExploreDbContext context, string eventTitle)
    {
        var tenant = new Tenant
        {
            FullName = "SoftDel Test Tenant",
            Slug = "softdel-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = 2,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"softdel-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "SoftDel",
                LastName = "Tester"
            }
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "SoftDel Test Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = eventTitle,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return (tenant.Id, actor.Id, @event.Id);
    }

    #endregion
}
