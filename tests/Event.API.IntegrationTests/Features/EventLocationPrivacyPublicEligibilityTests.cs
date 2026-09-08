using System.Net;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[Category("EventLocationPrivacy")]
[Category("EventLocationPrivacyApi")]
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public sealed class EventLocationPrivacyPublicEligibilityTests(RealRuntimeApiFixture fixture)
{
    [Test]
    public async Task AnonymousSiblingReads_HideDraftPrivateAndModeratedParentGraphs()
    {
        await fixture.ResetDatabaseAsync();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        PublicGraph[] hiddenGraphs =
        [
            await SeedGraphAsync(context, tenant, "draft-parent", EventStatusEnum.Draft, VisibilityTypeEnum.Public),
            await SeedGraphAsync(context, tenant, "private-parent", EventStatusEnum.Published, VisibilityTypeEnum.Private),
            await SeedGraphAsync(context, tenant, "moderated-parent", EventStatusEnum.Moderated, VisibilityTypeEnum.Public)
        ];

        foreach (PublicGraph graph in hiddenGraphs)
        {
            await AssertCollectionOmitsAsync($"/api/eventsessiongroup/by-event/{graph.EventId}", graph.GroupMarker);
            await AssertNotFoundAsync($"/api/eventsessiongroup/{graph.GroupId}", graph.GroupMarker);
            await AssertCollectionOmitsAsync($"/api/eventsessiongroup/{graph.GroupId}/sessions", graph.SessionMarker);

            await AssertCollectionOmitsAsync($"/api/eventagendaitem/by-event/{graph.EventId}", graph.AgendaMarker);
            await AssertNotFoundAsync($"/api/eventagendaitem/{graph.AgendaItemId}", graph.AgendaMarker);

            await AssertCollectionOmitsAsync($"/api/eventsessionagendaitem/by-session/{graph.SessionId}", graph.SessionAgendaMarker);
            await AssertNotFoundAsync($"/api/eventsessionagendaitem/{graph.SessionAgendaItemId}", graph.SessionAgendaMarker);
        }

        using var rootResponse = await fixture.Client.GetAsync("/api/eventsessionagendaitem?pageNumber=1&pageSize=100");
        string rootBody = await rootResponse.Content.ReadAsStringAsync();

        await Assert.That(rootResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        foreach (PublicGraph graph in hiddenGraphs)
            await Assert.That(rootBody).DoesNotContain(graph.SessionAgendaMarker);
    }

    [Test]
    public async Task AnonymousSiblingReads_HideUnpublishedChildrenAndKeepPublishedPublicControls()
    {
        await fixture.ResetDatabaseAsync();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        PublicGraph visible = await SeedGraphAsync(
            context,
            tenant,
            "published-control",
            EventStatusEnum.Published,
            VisibilityTypeEnum.Public);
        PublicGraph unpublishedGroup = await SeedGraphAsync(
            context,
            tenant,
            "unpublished-group",
            EventStatusEnum.Published,
            VisibilityTypeEnum.Public,
            groupIsPublished: false);
        PublicGraph unpublishedSession = await SeedGraphAsync(
            context,
            tenant,
            "unpublished-session",
            EventStatusEnum.Published,
            VisibilityTypeEnum.Public,
            sessionStatus: EventSessionStatusEnum.Draft);
        PublicGraph unpublishedDay = await SeedGraphAsync(
            context,
            tenant,
            "unpublished-day",
            EventStatusEnum.Published,
            VisibilityTypeEnum.Public,
            eventDayIsPublished: false);

        await AssertCollectionContainsAsync($"/api/eventsessiongroup/by-event/{visible.EventId}", visible.GroupMarker);
        await AssertOkContainsAsync($"/api/eventsessiongroup/{visible.GroupId}", visible.GroupMarker);
        await AssertCollectionContainsAsync($"/api/eventsessiongroup/{visible.GroupId}/sessions", visible.SessionMarker);
        await AssertCollectionContainsAsync($"/api/eventagendaitem/by-event/{visible.EventId}", visible.AgendaMarker);
        await AssertOkContainsAsync($"/api/eventagendaitem/{visible.AgendaItemId}", visible.AgendaMarker);
        await AssertCollectionContainsAsync($"/api/eventsessionagendaitem/by-session/{visible.SessionId}", visible.SessionAgendaMarker);
        await AssertOkContainsAsync($"/api/eventsessionagendaitem/{visible.SessionAgendaItemId}", visible.SessionAgendaMarker);

        await AssertCollectionOmitsAsync($"/api/eventsessiongroup/by-event/{unpublishedGroup.EventId}", unpublishedGroup.GroupMarker);
        await AssertNotFoundAsync($"/api/eventsessiongroup/{unpublishedGroup.GroupId}", unpublishedGroup.GroupMarker);
        await AssertCollectionOmitsAsync($"/api/eventsessiongroup/{unpublishedGroup.GroupId}/sessions", unpublishedGroup.SessionMarker);

        await AssertCollectionOmitsAsync($"/api/eventsessiongroup/{unpublishedSession.GroupId}/sessions", unpublishedSession.SessionMarker);
        await AssertCollectionOmitsAsync($"/api/eventsessionagendaitem/by-session/{unpublishedSession.SessionId}", unpublishedSession.SessionAgendaMarker);
        await AssertNotFoundAsync($"/api/eventsessionagendaitem/{unpublishedSession.SessionAgendaItemId}", unpublishedSession.SessionAgendaMarker);

        await AssertCollectionOmitsAsync($"/api/eventsession/by-event/{unpublishedDay.EventId}", unpublishedDay.SessionMarker);
        await AssertNotFoundAsync($"/api/eventsession/{unpublishedDay.SessionId}", unpublishedDay.SessionMarker);
        await AssertCollectionOmitsAsync($"/api/eventsessiongroup/{unpublishedDay.GroupId}/sessions", unpublishedDay.SessionMarker);
        await AssertCollectionOmitsAsync($"/api/eventagendaitem/by-event/{unpublishedDay.EventId}", unpublishedDay.AgendaMarker);
        await AssertNotFoundAsync($"/api/eventagendaitem/{unpublishedDay.AgendaItemId}", unpublishedDay.AgendaMarker);
        await AssertCollectionOmitsAsync($"/api/eventsessionagendaitem/by-session/{unpublishedDay.SessionId}", unpublishedDay.SessionAgendaMarker);
        await AssertNotFoundAsync($"/api/eventsessionagendaitem/{unpublishedDay.SessionAgendaItemId}", unpublishedDay.SessionAgendaMarker);
        await AssertCollectionOmitsAsync($"/api/eventagendaitem/agenda-projection/{unpublishedDay.EventId}", unpublishedDay.DayMarker!);
        await AssertCollectionOmitsAsync($"/api/eventagendaitem/agenda-projection/{unpublishedDay.EventId}", unpublishedDay.SessionMarker);
        await AssertCollectionOmitsAsync($"/api/event/{unpublishedGroup.EventId}/program-summary", unpublishedGroup.GroupMarker);

        using var programResponse = await fixture.Client.GetAsync($"/api/event/{visible.EventId}/program-summary");
        string programBody = await programResponse.Content.ReadAsStringAsync();
        await Assert.That(programResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(programBody).DoesNotContain("locationId");
        await Assert.That(programBody).DoesNotContain("no location or room assigned");

        using var rootResponse = await fixture.Client.GetAsync("/api/eventsessionagendaitem?pageNumber=1&pageSize=100");
        string rootBody = await rootResponse.Content.ReadAsStringAsync();

        await Assert.That(rootResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(rootBody).Contains(visible.SessionAgendaMarker);
        await Assert.That(rootBody).DoesNotContain(unpublishedSession.SessionAgendaMarker);
    }

    private async Task AssertCollectionContainsAsync(string route, string marker)
    {
        using var response = await fixture.Client.GetAsync(route);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains(marker);
    }

    private async Task AssertCollectionOmitsAsync(string route, string marker)
    {
        using var response = await fixture.Client.GetAsync(route);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).DoesNotContain(marker);
    }

    private async Task AssertOkContainsAsync(string route, string marker)
    {
        using var response = await fixture.Client.GetAsync(route);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains(marker);
    }

    private async Task AssertNotFoundAsync(string route, string marker)
    {
        using var response = await fixture.Client.GetAsync(route);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).DoesNotContain(marker);
    }

    private static async Task<PublicGraph> SeedGraphAsync(
        ExploreDbContext context,
        TenantScenarioSeed.TenantScenarioResult tenant,
        string label,
        EventStatusEnum eventStatus,
        VisibilityTypeEnum visibility,
        bool groupIsPublished = true,
        EventSessionStatusEnum sessionStatus = EventSessionStatusEnum.Published,
        bool? eventDayIsPublished = null)
    {
        string marker = $"ELP-{label}-{Guid.NewGuid():N}";
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var @event = new EventBuilder()
            .WithTitle($"Event {marker}")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(eventStatus)
            .WithVisibility(visibility)
            .WithSessionDates(DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime))
            .Build();
        var session = new EventSession(sessionStatus)
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenant.TenantId,
            Tenant = null!,
            Title = $"Session {marker}",
            SortOrder = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());

        EventDay? eventDay = eventDayIsPublished.HasValue
            ? new EventDay
            {
                Id = Guid.CreateVersion7(),
                EventId = @event.Id,
                Event = @event,
                LocalDate = DateOnly.FromDateTime(start.UtcDateTime),
                Label = $"Day {marker}",
                IsPublished = eventDayIsPublished.Value,
                TenantId = tenant.TenantId,
                Tenant = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            }
            : null;
        session.EventDayId = eventDay?.Id;
        session.EventDay = eventDay;

        var group = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            Name = $"Group {marker}",
            IsPublished = groupIsPublished,
            TenantId = tenant.TenantId,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var assignment = new EventSessionGroupSession
        {
            Id = Guid.CreateVersion7(),
            EventSessionGroupId = group.Id,
            EventSessionGroup = group,
            EventSessionId = session.Id,
            EventSession = session,
            EventId = @event.Id,
            Event = @event,
            IsPrimary = true,
            TenantId = tenant.TenantId,
            Tenant = null!
        };
        var agendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            Title = $"Agenda {marker}",
            TenantId = tenant.TenantId,
            Tenant = null!,
            SortOrder = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        agendaItem.EventDayId = eventDay?.Id;
        agendaItem.EventDay = eventDay;
        agendaItem.Reschedule(UtcInstantRange.Create(start.AddHours(1), start.AddHours(2)), "UTC", new EventScheduleProjectionCalculator());
        var sessionAgendaItem = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = session.Id,
            EventSession = session,
            StartTime = start.AddMinutes(10),
            EndTime = start.AddMinutes(20),
            Title = $"Session agenda {marker}",
            TenantId = tenant.TenantId,
            Tenant = null!
        };

        context.Events.Add(@event);
        if (eventDay is not null)
            context.EventDays.Add(eventDay);
        context.EventSessions.Add(session);
        context.EventSessionGroups.Add(group);
        context.EventSessionGroupSessions.Add(assignment);
        context.EventAgendaItems.Add(agendaItem);
        context.EventSessionAgendaItems.Add(sessionAgendaItem);
        await context.SaveChangesAsync();

        return new PublicGraph(
            @event.Id,
            session.Id,
            group.Id,
            agendaItem.Id,
            sessionAgendaItem.Id,
            group.Name,
            session.Title!,
            agendaItem.Title,
            sessionAgendaItem.Title,
            eventDay?.Label);
    }

    private sealed record PublicGraph(
        Guid EventId,
        Guid SessionId,
        Guid GroupId,
        Guid AgendaItemId,
        Guid SessionAgendaItemId,
        string GroupMarker,
        string SessionMarker,
        string AgendaMarker,
        string SessionAgendaMarker,
        string? DayMarker);
}
