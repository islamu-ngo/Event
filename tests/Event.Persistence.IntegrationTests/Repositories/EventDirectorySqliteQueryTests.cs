using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDirectorySqliteQueryTests
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(TemporalView.Upcoming, "future")]
    [Arguments(TemporalView.Ongoing, "current,ends-after,starts-now")]
    [Arguments(TemporalView.Past, "ends-before,ends-now,past")]
    [Arguments(TemporalView.UpcomingAndOngoing, "current,ends-after,future,starts-now")]
    [Arguments(TemporalView.All, "current,ends-after,ends-before,ends-now,future,past,starts-now,unscheduled")]
    public async Task Directory_temporal_views_preserve_exact_instant_boundaries(TemporalView view, string expected)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var specification = PublicDirectory()
            .And(EventSubqueryFilter.Temporal(view, Now))
            .SortBy(EventSort.Title);

        var (items, total) = await new EventRepository(fixture.Context)
            .GetEventsWithDetailsPaged(1, 20, specification);

        await Assert.That(string.Join(',', items.Select(item => item.Title))).IsEqualTo(expected);
        await Assert.That(total).IsEqualTo(expected.Split(',').Length);
        await Assert.That(items.All(item => item.EventStatus is not null && item.Actor is not null)).IsTrue();
        await Assert.That(fixture.Context.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Directory_count_and_stable_paging_keep_visibility_and_tenant_filters()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var repository = new EventRepository(fixture.Context);
        var specification = PublicDirectory()
            .And(EventSubqueryFilter.Temporal(TemporalView.UpcomingAndOngoing, Now))
            .SortBy(EventSort.Title);

        var (items, total) = await repository.GetEventsWithDetailsPaged(2, 2, specification);
        await Assert.That(total).IsEqualTo(4);
        await Assert.That(string.Join(',', items.Select(item => item.Title))).IsEqualTo("future,starts-now");

        fixture.Context.TenantContext = null;
        var (unscoped, unscopedTotal) = await repository.GetEventsWithDetailsPaged(1, 20, specification);
        await Assert.That(unscoped.Count).IsEqualTo(0);
        await Assert.That(unscopedTotal).IsEqualTo(0);
    }

    [Test]
    public async Task Directory_instant_translation_is_registered_on_each_query_connection()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        // Keep both connections open so pooling cannot turn this into a single-connection test.
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        var specification = PublicDirectory()
            .And(EventSubqueryFilter.Temporal(TemporalView.Ongoing, Now))
            .SortBy(EventSort.Title);

        foreach (var context in new[] { first, second })
        {
            var (items, total) = await new EventRepository(context)
                .GetEventsWithDetailsPaged(1, 20, specification);
            await Assert.That(total).IsEqualTo(3);
            await Assert.That(string.Join(',', items.Select(item => item.Title)))
                .IsEqualTo("current,ends-after,starts-now");
        }
    }

    [Test]
    public async Task Default_directory_excludes_ended_events_and_pages_date_ties_by_id()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var repository = new EventRepository(fixture.Context, new FixedTimeProvider());
        var specification = PublicDirectory()
            .And(EventSubqueryFilter.CurrentOrUpcomingPublishedSession())
            .SortByDescending(EventSort.Date);

        var (firstPage, total) = await repository.GetEventsWithDetailsPaged(1, 2, specification);
        var (secondPage, secondTotal) = await repository.GetEventsWithDetailsPaged(2, 2, specification);
        await Assert.That(total).IsEqualTo(4);
        await Assert.That(secondTotal).IsEqualTo(4);
        await Assert.That(string.Join(',', firstPage.Select(item => item.Title))).IsEqualTo("ends-after,current");
        await Assert.That(string.Join(',', secondPage.Select(item => item.Title))).IsEqualTo("starts-now,future");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static EventQuerySpecification PublicDirectory() => new EventQuerySpecification()
        .And(EventFilter.PubliclyDiscoverable())
        .And(EventFilter.Status((int)EventStatusEnum.Published));

    private static async Task SeedAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var eventNumber = 0;
        var otherTenantId = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = otherTenantId,
            FullName = "Other directory",
            Slug = $"other-{otherTenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        fixture.Context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = otherTenantId,
            Tenant = null!,
            UserId = fixture.UserId,
            User = null!,
            ActorId = fixture.ActorId,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = Now.UtcDateTime,
            CreatedAt = Now.UtcDateTime
        });
        Add("past", Now.AddDays(-2), Now.AddDays(-1));
        Add("ends-before", Now.AddHours(-1), Now.AddTicks(-1));
        Add("ends-now", Now.AddHours(-1), Now.ToOffset(TimeSpan.FromHours(5.5)));
        Add("ends-after", Now.AddHours(-1), Now.AddTicks(1).ToOffset(TimeSpan.FromHours(-4)));
        Add("current", Now.AddHours(-1), Now.AddHours(1));
        Add("starts-now", Now.ToOffset(TimeSpan.FromHours(9)), Now.AddHours(1));
        Add("future", Now.AddTicks(1).ToOffset(TimeSpan.FromHours(-7)), Now.AddDays(1));
        Add("unscheduled", null, null);
        Add("private", Now, Now.AddDays(1)).VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        Add("deleted", Now, Now.AddDays(1)).IsDeleted = true;
        Add("draft", Now, Now.AddDays(1), EventStatusEnum.Draft);
        Add("other-tenant", Now, Now.AddDays(1)).TenantId = otherTenantId;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Explore.Domain.Event Add(string title, DateTimeOffset? start, DateTimeOffset? end,
            EventStatusEnum status = EventStatusEnum.Published)
        {
            var entity = new Explore.Domain.Event(status)
            {
                Id = Guid.Parse($"018e4e5c-7f00-7000-8000-{++eventNumber:x12}"),
                Title = title,
                PublicCode = title,
                TenantId = fixture.TenantId,
                Tenant = null!,
                ActorId = fixture.ActorId,
                Actor = null!,
                OrganizerActorId = fixture.ActorId,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventStatus = null!,
                SessionCount = start.HasValue ? 1 : 0,
                FirstSessionStartUtc = start,
                LastSessionStartUtc = start,
                LastSessionEndUtc = end,
                FirstSessionDate = start.HasValue ? DateOnly.FromDateTime(start.Value.DateTime) : null,
                CreatedAt = Now.UtcDateTime
            };
            fixture.Context.Events.Add(entity);
            return entity;
        }
    }
}
