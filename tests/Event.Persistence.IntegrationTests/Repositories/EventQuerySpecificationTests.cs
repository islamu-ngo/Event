using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>
/// Tests EventQuerySpecification filter and sort behavior on real PostgreSQL
/// via EventRepository.GetEventsWithDetailsPaged. Each test resets the database
/// for deterministic state.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EventQuerySpecificationTests(PostgreSqlContainerFixture fixture)
{
    private readonly PostgreSqlContainerFixture _fixture = fixture;

    [Test]
    public async Task Filter_ByStatus_ReturnsOnlyMatchingStatus()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var publishedEvent = CreateEvent(tenantId, actorId, "Published Event",
            statusId: (int)EventStatusEnum.Published);
        var draftEvent = CreateEvent(tenantId, actorId, "Draft Event",
            statusId: (int)EventStatusEnum.Draft);
        context.Events.AddRange(publishedEvent, draftEvent);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.Status((int)EventStatusEnum.Published));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items).Count().IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Published Event");
    }

    [Test]
    public async Task Filter_ByFormat_ReturnsOnlyMatchingFormat()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var localEvent = CreateEvent(tenantId, actorId, "Local Event",
            formatId: (int)EventFormatEnum.Local);
        var digitalEvent = CreateEvent(tenantId, actorId, "Digital Event",
            formatId: (int)EventFormatEnum.Digital);
        context.Events.AddRange(localEvent, digitalEvent);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.Format((int)EventFormatEnum.Local));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items).Count().IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Local Event");
    }

    [Test]
    public async Task Filter_BySearchTerm_MatchesTitleContaining()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var workshopEvent = CreateEvent(tenantId, actorId, "Advanced Workshop on AI");
        var conferenceEvent = CreateEvent(tenantId, actorId, "Tech Conference 2026");
        context.Events.AddRange(workshopEvent, conferenceEvent);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.SearchTerm("Workshop"));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items[0].Title).Contains("Workshop");
    }

    [Test]
    public async Task Filter_ByDateRange_ReturnsEventsInRange()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var pastEvent = CreateEvent(tenantId, actorId, "Past Event",
            firstSessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
        var futureEvent = CreateEvent(tenantId, actorId, "Future Event",
            firstSessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        context.Events.AddRange(pastEvent, futureEvent);
        await context.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var spec = new EventQuerySpecification()
            .And(EventFilter.DateFrom(today));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Future Event");
    }

    [Test]
    public async Task Filter_PubliclyDiscoverable_ExcludesDraftArchivedAndNonPublic()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var publishedPublic = CreateEvent(tenantId, actorId, "Published Public",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        var draftPublic = CreateEvent(tenantId, actorId, "Draft Public",
            statusId: (int)EventStatusEnum.Draft, visibilityId: (int)VisibilityTypeEnum.Public);
        var archivedPublic = CreateEvent(tenantId, actorId, "Archived Public",
            statusId: (int)EventStatusEnum.Archived, visibilityId: (int)VisibilityTypeEnum.Public);
        var publishedPrivate = CreateEvent(tenantId, actorId, "Published Private",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Private);
        context.Events.AddRange(publishedPublic, draftPublic, archivedPublic, publishedPrivate);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.PubliclyDiscoverable());
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Published Public");
    }

    [Test]
    public async Task Sort_ByTitleAscending_ReturnsAlphabeticalOrder()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var eventC = CreateEvent(tenantId, actorId, "Charlie Event");
        var eventA = CreateEvent(tenantId, actorId, "Alpha Event");
        var eventB = CreateEvent(tenantId, actorId, "Bravo Event");
        context.Events.AddRange(eventC, eventA, eventB);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .SortBy(EventSort.Title);
        var repository = new EventRepository(context);
        var (items, _) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(items).Count().IsEqualTo(3);
        await Assert.That(items[0].Title).IsEqualTo("Alpha Event");
        await Assert.That(items[1].Title).IsEqualTo("Bravo Event");
        await Assert.That(items[2].Title).IsEqualTo("Charlie Event");
    }

    [Test]
    public async Task Sort_ByDateDescending_ReturnsNewestFirst()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var oldEvent = CreateEvent(tenantId, actorId, "Old Event",
            firstSessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)));
        var newEvent = CreateEvent(tenantId, actorId, "New Event",
            firstSessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        context.Events.AddRange(oldEvent, newEvent);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .SortByDescending(EventSort.Date);
        var repository = new EventRepository(context);
        var (items, _) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(items).Count().IsEqualTo(2);
        await Assert.That(items[0].Title).IsEqualTo("New Event");
        await Assert.That(items[1].Title).IsEqualTo("Old Event");
    }

    [Test]
    public async Task CombinedFilters_StatusAndFormat_AppliesBothConditions()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var publishedLocal = CreateEvent(tenantId, actorId, "Published Local",
            statusId: (int)EventStatusEnum.Published, formatId: (int)EventFormatEnum.Local);
        var publishedDigital = CreateEvent(tenantId, actorId, "Published Digital",
            statusId: (int)EventStatusEnum.Published, formatId: (int)EventFormatEnum.Digital);
        var draftLocal = CreateEvent(tenantId, actorId, "Draft Local",
            statusId: (int)EventStatusEnum.Draft, formatId: (int)EventFormatEnum.Local);
        context.Events.AddRange(publishedLocal, publishedDigital, draftLocal);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.Status((int)EventStatusEnum.Published))
            .And(EventFilter.Format((int)EventFormatEnum.Local));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Published Local");
    }

    [Test]
    public async Task PublicDiscoveryLocationFilter_UsesOnlyPublishedScheduledSessions()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);

        var location = new Location
        {
            FullName = "Public Venue",
            Country = "BE",
            City = "Brussels",
            TenantId = tenantId,
            Tenant = null!
        };
        location.SetManualAddress("Public Street 1", "1000");
        context.Locations.Add(location);

        var hiddenOnlyEvent = CreateEvent(
            tenantId,
            actorId,
            "Hidden Session Only",
            statusId: (int)EventStatusEnum.Published,
            visibilityId: (int)VisibilityTypeEnum.Public);
        var publicSessionEvent = CreateEvent(
            tenantId,
            actorId,
            "Public Session",
            statusId: (int)EventStatusEnum.Published,
            visibilityId: (int)VisibilityTypeEnum.Public);
        context.Events.AddRange(hiddenOnlyEvent, publicSessionEvent);
        await context.SaveChangesAsync();

        var actorUserId = (await context.Actors.FindAsync(actorId))?.UserId
            ?? throw new InvalidOperationException("The public-discovery fixture actor must have a user identity.");
        var hiddenEventLocation = EventLocation.CreatePhysical(
            tenantId,
            hiddenOnlyEvent.Id,
            location.Id,
            actorUserId,
            DateTime.UtcNow);
        var publicEventLocation = EventLocation.CreatePhysical(
            tenantId,
            publicSessionEvent.Id,
            location.Id,
            actorUserId,
            DateTime.UtcNow);
        context.EventLocations.AddRange(hiddenEventLocation, publicEventLocation);
        await context.SaveChangesAsync();

        var hiddenSession = CreateSession(
            tenantId,
            hiddenOnlyEvent.Id,
            null,
            EventSessionStatusEnum.Draft,
            new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero));
        hiddenSession.AssignEventLocation(hiddenEventLocation);
        var publicSession = CreateSession(
            tenantId,
            publicSessionEvent.Id,
            null,
            EventSessionStatusEnum.Published,
            new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero));
        publicSession.AssignEventLocation(publicEventLocation);
        context.EventSessions.AddRange(hiddenSession, publicSession);
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.PubliclyDiscoverable())
            .And(EventSubqueryFilter.Locations([location.Id]));
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items).Count().IsEqualTo(1);
        await Assert.That(items[0].Title).IsEqualTo("Public Session");
    }

    [Test]
    public async Task PublicDiscoveryScheduleFilter_ExcludesEventsWhosePublishedSessionsHaveEnded()
    {
        await _fixture.ResetAsync();
        using var context = _fixture.CreateDbContext();
        var (tenantId, actorId) = await SeedBaseEntities(context);
        var now = DateTimeOffset.UtcNow;

        var endedSingleSession = CreateEvent(tenantId, actorId, "Ended Single Session",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        var endedMultiSession = CreateEvent(tenantId, actorId, "Ended Multi Session",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        var ongoingSession = CreateEvent(tenantId, actorId, "Ongoing Session",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        var futureMultiSession = CreateEvent(tenantId, actorId, "Future Multi Session",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        var draftFutureSession = CreateEvent(tenantId, actorId, "Draft Future Session",
            statusId: (int)EventStatusEnum.Published, visibilityId: (int)VisibilityTypeEnum.Public);
        context.Events.AddRange(endedSingleSession, endedMultiSession, ongoingSession, futureMultiSession, draftFutureSession);
        await context.SaveChangesAsync();

        var endedSingleSessionRows = new[]
        {
            CreateSession(tenantId, endedSingleSession.Id, null, EventSessionStatusEnum.Published, now.AddDays(-2), now.AddDays(-2).AddHours(1))
        };
        var endedMultiSessionRows = new[]
        {
            CreateSession(tenantId, endedMultiSession.Id, null, EventSessionStatusEnum.Published, now.AddDays(-3), now.AddDays(-3).AddHours(1)),
            CreateSession(tenantId, endedMultiSession.Id, null, EventSessionStatusEnum.Published, now.AddHours(-3), now.AddHours(-2))
        };
        var ongoingSessionRows = new[]
        {
            CreateSession(tenantId, ongoingSession.Id, null, EventSessionStatusEnum.Published, now.AddHours(-1), now.AddHours(1))
        };
        var futureMultiSessionRows = new[]
        {
            CreateSession(tenantId, futureMultiSession.Id, null, EventSessionStatusEnum.Published, now.AddDays(-1), now.AddDays(-1).AddHours(1)),
            CreateSession(tenantId, futureMultiSession.Id, null, EventSessionStatusEnum.Published, now.AddDays(2), now.AddDays(2).AddHours(1))
        };
        var draftFutureSessionRows = new[]
        {
            CreateSession(tenantId, draftFutureSession.Id, null, EventSessionStatusEnum.Draft, now.AddDays(1), now.AddDays(1).AddHours(1))
        };

        AttachSessionsAndRecalculate(endedSingleSession, endedSingleSessionRows);
        AttachSessionsAndRecalculate(endedMultiSession, endedMultiSessionRows);
        AttachSessionsAndRecalculate(ongoingSession, ongoingSessionRows);
        AttachSessionsAndRecalculate(futureMultiSession, futureMultiSessionRows);
        AttachSessionsAndRecalculate(draftFutureSession, draftFutureSessionRows);

        context.EventSessions.AddRange(
            endedSingleSessionRows
                .Concat(endedMultiSessionRows)
                .Concat(ongoingSessionRows)
                .Concat(futureMultiSessionRows)
                .Concat(draftFutureSessionRows));
        await context.SaveChangesAsync();

        var spec = new EventQuerySpecification()
            .And(EventFilter.PubliclyDiscoverable())
            .And(EventSubqueryFilter.CurrentOrUpcomingPublishedSession())
            .SortBy(EventSort.Title);
        var repository = new EventRepository(context);
        var (items, totalCount) = await repository.GetEventsWithDetailsPaged(1, 10, spec);

        await Assert.That(totalCount).IsEqualTo(2);
        await Assert.That(items.Select(item => item.Title)).IsEquivalentTo(["Future Multi Session", "Ongoing Session"]);
    }

    #region Helpers

    private static async Task<(Guid TenantId, Guid ActorId)> SeedBaseEntities(ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            FullName = "Spec Test Tenant",
            Slug = "spec-test-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = 2,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"spec-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Spec",
                LastName = "Tester"
            }
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Spec Test Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        await context.SaveChangesAsync();

        return (tenant.Id, actor.Id);
    }

    private static Explore.Domain.Event CreateEvent(
        Guid tenantId, Guid actorId, string title,
        int statusId = (int)EventStatusEnum.Draft,
        int formatId = (int)EventFormatEnum.Local,
        int visibilityId = (int)VisibilityTypeEnum.Public,
        DateOnly? firstSessionDate = null)
    {
        return new Explore.Domain.Event((EventStatusEnum)statusId)
        {
            Id = Guid.NewGuid(),
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actorId,
            Actor = null!,
            TenantId = tenantId,
            Tenant = null!,
            EventStatus = null!,
            VisibilityTypeId = visibilityId,
            VisibilityType = null!,
            EventFormatId = formatId,
            EventFormat = null!,
            TotalViews = 0,
            FirstSessionDate = firstSessionDate,
            ConcurrencyStamp = Guid.NewGuid()
        };
    }

    private static EventSession CreateSession(
        Guid tenantId,
        Guid eventId,
        Guid? locationId,
        EventSessionStatusEnum status,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt = null)
    {
        var session = new EventSession(status)
        {
            EventId = eventId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            LocationId = locationId,
            Location = null!,
            Title = status == EventSessionStatusEnum.Published ? "Published session" : "Hidden session"
        };

        session.Reschedule(UtcInstantRange.Create(startsAt, endsAt ?? startsAt.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
        return session;
    }

    private static void AttachSessionsAndRecalculate(Explore.Domain.Event eventEntity, IEnumerable<EventSession> sessions)
    {
        foreach (var session in sessions)
        {
            session.Event = eventEntity;
            eventEntity.Sessions.Add(session);
        }

        eventEntity.RecalculateScheduleSummaryFromSessions();
    }

    #endregion
}
