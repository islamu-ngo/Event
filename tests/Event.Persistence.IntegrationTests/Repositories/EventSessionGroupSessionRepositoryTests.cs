// ABOUTME: Persistence regression tests for EventSessionGroupSession soft-delete uniqueness behavior.
// ABOUTME: Verifies reassignment after soft-delete uses active-row indexes, not stale deleted memberships.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventSessionGroupSessionRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EventSessionGroupSessionRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task SoftDeletedMembership_ShouldNotBlockReassignmentToSameGroup()
    {
        using var context = _fixture.CreateDbContext();
        var (_, @event, session, group) = await SetupSessionGroupAsync(context);
        var repository = new EventSessionGroupSessionRepository(context);

        var firstAssignment = CreateAssignment(@event, session, group, isPrimary: false, sortOrder: 1);
        await repository.Create(firstAssignment);

        await repository.Delete(firstAssignment);

        var replacementAssignment = CreateAssignment(@event, session, group, isPrimary: false, sortOrder: 2);
        var result = await repository.Create(replacementAssignment);

        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        var activeAssignments = await context.EventSessionGroupSessions
            .Where(assignment => assignment.EventSessionGroupId == group.Id && assignment.EventSessionId == session.Id)
            .ToListAsync();

        await Assert.That(activeAssignments.Count).IsEqualTo(1);
        await Assert.That(activeAssignments[0].SortOrder).IsEqualTo(2);
    }

    [Test]
    public async Task SoftDeletedPrimaryMembership_ShouldNotBlockNewPrimaryAssignment()
    {
        using var context = _fixture.CreateDbContext();
        var (_, @event, session, firstGroup) = await SetupSessionGroupAsync(context);
        var secondGroup = new EventSessionGroup
        {
            EventId = @event.Id,
            Event = null!,
            Name = "Second Track",
            SortOrder = 2,
            IsPublished = false,
            TenantId = @event.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventSessionGroups.Add(secondGroup);
        await context.SaveChangesAsync();

        var repository = new EventSessionGroupSessionRepository(context);
        var firstPrimary = CreateAssignment(@event, session, firstGroup, isPrimary: true, sortOrder: 1);
        await repository.Create(firstPrimary);

        await repository.Delete(firstPrimary);

        var replacementPrimary = CreateAssignment(@event, session, secondGroup, isPrimary: true, sortOrder: 1);
        var result = await repository.Create(replacementPrimary);

        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        var activePrimaryAssignments = await context.EventSessionGroupSessions
            .Where(assignment => assignment.EventSessionId == session.Id && assignment.IsPrimary)
            .ToListAsync();

        await Assert.That(activePrimaryAssignments.Count).IsEqualTo(1);
        await Assert.That(activePrimaryAssignments[0].EventSessionGroupId).IsEqualTo(secondGroup.Id);
    }

    private static EventSessionGroupSession CreateAssignment(
        Explore.Domain.Event @event,
        EventSession session,
        EventSessionGroup group,
        bool isPrimary,
        int sortOrder)
    {
        return new EventSessionGroupSession
        {
            EventId = @event.Id,
            Event = null!,
            EventSessionId = session.Id,
            EventSession = null!,
            EventSessionGroupId = group.Id,
            EventSessionGroup = null!,
            IsPrimary = isPrimary,
            SortOrder = sortOrder,
            TenantId = @event.TenantId,
            Tenant = null!
        };
    }

    private static async Task<(Tenant tenant, Explore.Domain.Event @event, EventSession session, EventSessionGroup group)> SetupSessionGroupAsync(
        ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Session Group Tenant",
            Slug = "session-group-tenant-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"session-group-{Guid.NewGuid():N}@example.com",
                FirstName = "Session",
                LastName = "Owner"
            }
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Session Group Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Session Group Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.Events.Add(@event);

        var session = new EventSession
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            Title = "Session Group Talk",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventSessions.Add(session);

        var group = new EventSessionGroup
        {
            EventId = @event.Id,
            Event = null!,
            Name = "Main Track",
            SortOrder = 1,
            IsPublished = false,
            TenantId = tenant.Id,
            Tenant = null!,
            ConcurrencyStamp = Guid.NewGuid()
        };
        context.EventSessionGroups.Add(group);

        await context.SaveChangesAsync();

        return (tenant, @event, session, group);
    }
}
