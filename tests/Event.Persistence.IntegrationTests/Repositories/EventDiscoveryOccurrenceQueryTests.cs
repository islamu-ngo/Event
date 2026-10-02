using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryOccurrenceQueryTests
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2028, 6, 15);

    [Test]
    public async Task Dates_and_governed_area_must_match_one_published_occurrence()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var crossed = AddEvent(fixture, "crossed");
        var matching = AddEvent(fixture, "matching");
        var area = AddLocation(fixture);
        var elsewhere = AddLocation(fixture);
        var restrictedArea = AddLocation(fixture);
        AddSession(fixture, crossed, Now.AddYears(-1), Now.AddYears(-1).AddHours(1), area);
        AddSession(fixture, crossed, Now, Now.AddHours(1), elsewhere);
        var selected = AddSession(fixture, matching, Now, Now.AddHours(1), area);
        AddSession(fixture, matching, Now.AddMinutes(10), Now.AddHours(2), area);
        AddSession(fixture, matching, Now, Now.AddHours(1), area, EventSessionStatusEnum.Draft);
        var privateCarrier = AddSession(fixture, matching, Now.AddMinutes(-1), Now.AddHours(1), restrictedArea);
        privateCarrier.EventLocation!.ChangeDisclosurePolicy(EventLocationDisclosureFields.None,
            LocationDisclosureAudienceEnum.Never, null, privateCarrier.EventLocation.PolicyVersion,
            fixture.UserId, EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime);
        await SaveAsync(fixture);

        var locationIds = new List<Guid> { area.Id, restrictedArea.Id };
        var specification = Query(Today, Today, TemporalView.All, locationIds);
        locationIds.Clear();
        var (items, total) = await new EventRepository(fixture.Context).GetEventsWithDetailsPaged(
            1, 10, specification);

        await Assert.That(total).IsEqualTo(1);
        await Assert.That(items.Single().Id).IsEqualTo(matching.Id);
        await Assert.That(items.Single().Sessions.Single().Id).IsEqualTo(selected.Id);
        await Assert.That(items.Single().DiscoveryAdditionalSessionCount).IsEqualTo(1);
        await Assert.That(items.Single().Sessions.Single().Location).IsNull();
        await Assert.That(fixture.Context.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    [Arguments(TemporalView.Upcoming, "future")]
    [Arguments(TemporalView.Ongoing, "ends-after,open,starts-now")]
    [Arguments(TemporalView.Past, "ends-before,ends-now")]
    [Arguments(TemporalView.UpcomingAndOngoing, "ends-after,future,open,starts-now")]
    [Arguments(TemporalView.All, "ends-after,ends-before,ends-now,future,open,starts-now")]
    public async Task Translated_temporal_views_keep_exact_instants_and_explicit_open_ended(
        TemporalView view, string expected)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Add("ends-before", Now.AddHours(-1), Now.AddTicks(-1));
        Add("ends-now", Now.AddHours(-1), Now.ToOffset(TimeSpan.FromHours(5.5)));
        Add("ends-after", Now.AddHours(-1).ToOffset(TimeSpan.FromHours(-4)), Now.AddTicks(1));
        Add("starts-now", Now.ToOffset(TimeSpan.FromHours(9)), Now.AddHours(1));
        Add("future", Now.AddTicks(1).ToOffset(TimeSpan.FromHours(-7)), Now.AddDays(1));
        Add("open", Now.AddYears(-1), null, SessionEndTimeType.OpenEnded);
        Add("prayer", Now.AddDays(-1), null, SessionEndTimeType.RelativeToPrayer);
        var completed = Add("closed", Now.AddDays(-1), null, SessionEndTimeType.OpenEnded);
        completed.Complete(EventStatusEnum.Published, Now.UtcDateTime);
        await SaveAsync(fixture);

        var (items, total) = await new EventRepository(fixture.Context)
            .GetEventsWithDetailsPaged(1, 20, Query(null, null, view));

        await Assert.That(string.Join(',', items.Select(item => item.Title))).IsEqualTo(expected);
        await Assert.That(total).IsEqualTo(expected.Split(',').Length);
        await Assert.That(items.All(item => item.Sessions.Count == 1
            && item.DiscoveryAdditionalSessionCount == 0)).IsTrue();

        EventSession Add(string title, DateTimeOffset start, DateTimeOffset? end,
            SessionEndTimeType type = SessionEndTimeType.Fixed) =>
            AddSession(fixture, AddEvent(fixture, title), start, end, endType: type);
    }

    [Test]
    [Arguments(3, 26)]
    [Arguments(10, 29)]
    public async Task Inclusive_local_dates_and_midnight_end_translate_across_DST(int month, int day)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var date = new DateOnly(2028, month, day);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
        AddSession(fixture, AddEvent(fixture, "local-day"), start, end);
        await SaveAsync(fixture);
        var repository = new EventRepository(fixture.Context);

        var (items, total) = await repository.GetEventsWithDetailsPaged(1, 10, Query(date, date, TemporalView.All));
        var (after, afterTotal) = await repository.GetEventsWithDetailsPaged(
            1, 10, Query(date.AddDays(1), date.AddDays(1), TemporalView.All));

        await Assert.That(total).IsEqualTo(1);
        await Assert.That(items.Single().Sessions.Single().LocalStartDate).IsEqualTo(date);
        await Assert.That(after.Count).IsEqualTo(0);
        await Assert.That(afterTotal).IsEqualTo(0);
    }

    [Test]
    public async Task Current_area_policy_and_published_day_placement_are_required()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var location = AddLocation(fixture);
        var valid = AddSession(fixture, AddEvent(fixture, "valid"), Now, Now.AddHours(1), location);
        var hiddenCity = AddSession(fixture, AddEvent(fixture, "hidden-city"), Now, Now.AddHours(1), location);
        hiddenCity.EventLocation!.ChangeDisclosurePolicy(EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.Never, null, hiddenCity.EventLocation.PolicyVersion,
            fixture.UserId, EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime);
        var hiddenCountry = AddSession(fixture, AddEvent(fixture, "hidden-country"), Now, Now.AddHours(1), location);
        hiddenCountry.EventLocation!.ChangeDisclosurePolicy(EventLocationDisclosureFields.City,
            LocationDisclosureAudienceEnum.Never, null, hiddenCountry.EventLocation.PolicyVersion,
            fixture.UserId, EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime);
        var review = AddSession(fixture, AddEvent(fixture, "review"), Now, Now.AddHours(1), location);
        review.EventLocation!.ApplyGovernanceTightening(true, fixture.UserId, Now.UtcDateTime);
        var home = AddLocation(fixture);
        home.ClassifyAsPrivateHome(fixture.UserId);
        AddSession(fixture, AddEvent(fixture, "private-home"), Now, Now.AddHours(1), home);
        var unavailable = AddLocation(fixture, active: false);
        AddSession(fixture, AddEvent(fixture, "inactive"), Now, Now.AddHours(1), unavailable);
        var tbaEvent = AddEvent(fixture, "tba");
        var tba = EventLocation.CreateToBeAnnounced(fixture.TenantId, tbaEvent.Id, fixture.UserId, Now.UtcDateTime);
        fixture.Context.EventLocations.Add(tba);
        AddSession(fixture, tbaEvent, Now, Now.AddHours(1)).AssignEventLocation(tba);
        var hiddenDay = AddSession(fixture, AddEvent(fixture, "hidden-day"), Now, Now.AddHours(1), location);
        AddDay(hiddenDay, false, Today);
        var wrongDay = AddSession(fixture, AddEvent(fixture, "wrong-day"), Now, Now.AddHours(1), location);
        AddDay(wrongDay, true, Today.AddDays(1));
        AddDay(valid, true, Today);
        await SaveAsync(fixture);

        var (items, total) = await new EventRepository(fixture.Context).GetEventsWithDetailsPaged(
            1, 20, Query(Today, Today, TemporalView.All, [location.Id, home.Id, unavailable.Id]));
        await Assert.That(total).IsEqualTo(1);
        await Assert.That(items.Single().Sessions.Single().Id).IsEqualTo(valid.Id);

        void AddDay(EventSession session, bool published, DateOnly date)
        {
            var eventDay = new EventDay
            {
                Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
                EventId = session.EventId, Event = session.Event, IsPublished = published, LocalDate = date
            };
            fixture.Context.EventDays.Add(eventDay);
            session.EventDayId = eventDay.Id;
            session.EventDay = eventDay;
        }
    }

    [Test]
    public async Task Parent_eligibility_tenant_soft_delete_and_empty_area_fail_closed()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddSession(fixture, AddEvent(fixture, "valid"), Now, Now.AddHours(1));
        var privateEvent = AddEvent(fixture, "private");
        privateEvent.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        AddSession(fixture, privateEvent, Now, Now.AddHours(1));
        var deleted = AddEvent(fixture, "deleted");
        deleted.IsDeleted = true;
        AddSession(fixture, deleted, Now, Now.AddHours(1));
        AddSession(fixture, AddEvent(fixture, "deleted-session"), Now, Now.AddHours(1)).IsDeleted = true;
        var otherTenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Other occurrence tenant",
            Slug = $"occurrence-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        fixture.Context.Tenants.Add(otherTenant);
        fixture.Context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = otherTenant.Id, Tenant = otherTenant,
            UserId = fixture.UserId, User = null!, ActorId = fixture.ActorId,
            StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = Now.UtcDateTime
        });
        var foreign = AddEvent(fixture, "foreign");
        foreign.TenantId = otherTenant.Id;
        foreign.Tenant = otherTenant;
        var foreignSession = AddSession(fixture, foreign, Now, Now.AddHours(1));
        foreignSession.TenantId = otherTenant.Id;
        foreignSession.Tenant = otherTenant;
        await SaveAsync(fixture);
        var repository = new EventRepository(fixture.Context);

        var (items, total) = await repository.GetEventsWithDetailsPaged(1, 20, Query(null, null, TemporalView.All));
        await Assert.That(total).IsEqualTo(1);
        await Assert.That(items.Single().Title).IsEqualTo("valid");
        var (empty, emptyTotal) = await repository.GetEventsWithDetailsPaged(1, 20, Query(null, null, TemporalView.All, []));
        await Assert.That(empty.Count).IsEqualTo(0);
        await Assert.That(emptyTotal).IsEqualTo(0);

        fixture.Context.TenantContext = null;
        var (unscoped, unscopedTotal) = await repository.GetEventsWithDetailsPaged(1, 20, Query(null, null, TemporalView.All));
        await Assert.That(unscoped.Count).IsEqualTo(0);
        await Assert.That(unscopedTotal).IsEqualTo(0);
        fixture.Context.TenantContext = fixture;
        var actor = await fixture.Context.Actors.SingleAsync(item => item.Id == fixture.ActorId);
        actor.IsSuspended = true;
        await SaveAsync(fixture);
        var (suspended, suspendedTotal) = await repository.GetEventsWithDetailsPaged(1, 20, Query(null, null, TemporalView.All));
        await Assert.That(suspended.Count).IsEqualTo(0);
        await Assert.That(suspendedTotal).IsEqualTo(0);
    }

    [Test]
    public async Task Bounded_materialization_counts_matching_sessions_and_reuses_transaction_on_each_connection()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = AddEvent(fixture, "many");
        for (var index = 0; index < 35; index++)
        {
            AddSession(fixture, entity, Now.AddMinutes(index), Now.AddHours(2));
        }
        for (var index = 0; index < 7; index++)
        {
            AddSession(fixture, entity, Now, Now.AddHours(1), status: EventSessionStatusEnum.Draft);
        }
        await SaveAsync(fixture);
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        foreach (var context in new[] { first, second })
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var (items, total) = await new EventRepository(context).GetEventsWithDetailsPaged(
                1, 1, Query(null, null, TemporalView.All));
            await Assert.That(total).IsEqualTo(1);
            await Assert.That(items.Single().Sessions.Count).IsEqualTo(1);
            await Assert.That(items.Single().DiscoveryAdditionalSessionCount).IsEqualTo(34);
            await Assert.That(items.Single().Sessions.Single().StartTime).IsEqualTo(Now);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Occurrence_date_order_and_paging_ignore_unrelated_parent_summary_dates()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var earlier = AddEvent(fixture, "annual edition");
        var later = AddEvent(fixture, "annual edition");
        earlier.FirstSessionDate = Today.AddYears(1);
        later.FirstSessionDate = Today.AddYears(-1);
        AddSession(fixture, earlier, Now, Now.AddHours(1));
        AddSession(fixture, later, Now.AddHours(1), Now.AddHours(2));
        await SaveAsync(fixture);
        var repository = new EventRepository(fixture.Context);
        var specification = Query(Today, Today, TemporalView.All).SortBy(EventSort.Date);
        var (first, total) = await repository.GetEventsWithDetailsPaged(1, 1, specification);
        var (second, nextTotal) = await repository.GetEventsWithDetailsPaged(2, 1, specification);
        await Assert.That(first.Single().Id).IsEqualTo(earlier.Id);
        await Assert.That(second.Single().Id).IsEqualTo(later.Id);
        await Assert.That(total).IsEqualTo(2);
        await Assert.That(nextTotal).IsEqualTo(2);
    }

    private static EventQuerySpecification Query(DateOnly? from, DateOnly? to, TemporalView view,
        IReadOnlyList<Guid>? locationIds = null) =>
        new EventQuerySpecification()
            .WithOccurrence(new(from, to, view, Now, locationIds))
            .SortBy(EventSort.Title);

    private static Explore.Domain.Event AddEvent(EventVisitorCapabilitySqliteFixture fixture, string title)
    {
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            Title = title, PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = fixture.ActorId, Actor = null!, OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
            Timezone = "Europe/Brussels", CreatedAt = Now.UtcDateTime
        };
        fixture.Context.Events.Add(entity);
        return entity;
    }

    private static Location AddLocation(EventVisitorCapabilitySqliteFixture fixture, bool active = true)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId,
            FullName = "Public venue", City = "Brussels", Country = "BE"
        };
        if (active)
        {
            location.SetManualAddress(Guid.CreateVersion7().ToString("N"), "0000");
        }
        fixture.Context.Locations.Add(location);
        return location;
    }

    private static EventSession AddSession(EventVisitorCapabilitySqliteFixture fixture, Explore.Domain.Event entity,
        DateTimeOffset start, DateTimeOffset? end, Location? location = null,
        EventSessionStatusEnum status = EventSessionStatusEnum.Published,
        SessionEndTimeType endType = SessionEndTimeType.Fixed)
    {
        var session = new EventSession(status)
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            EventId = entity.Id, Event = entity, StartTime = start,
            EndTime = end?.ToOffset(start.Offset), EndTimeType = endType
        };
        session.ReprojectLocalTimes(entity.GetEffectiveScheduleTimeZoneId(), new EventScheduleProjectionCalculator());
        if (location is not null)
        {
            var carrier = fixture.Context.EventLocations.Local.SingleOrDefault(item =>
                item.EventId == entity.Id && item.LocationId == location.Id);
            if (carrier is null)
            {
                carrier = EventLocation.CreatePhysical(fixture.TenantId, entity.Id, location.Id, fixture.UserId, Now.UtcDateTime);
                carrier.ChangeDisclosurePolicy(EventLocationDisclosureFields.City | EventLocationDisclosureFields.Country,
                    LocationDisclosureAudienceEnum.Never, null, carrier.PolicyVersion, fixture.UserId,
                    EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime,
                    needsPrivacyReview: false);
                fixture.Context.EventLocations.Add(carrier);
            }
            session.AssignEventLocation(carrier);
        }
        fixture.Context.EventSessions.Add(session);
        return session;
    }

    private static async Task SaveAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }
}
