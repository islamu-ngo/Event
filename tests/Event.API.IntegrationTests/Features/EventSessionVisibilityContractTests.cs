using System.Net;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("ContractEventSessionVisibility")]
public class EventSessionVisibilityContractTests(ContractApiFixture fixture)
{
    private readonly ContractApiFixture _fixture = fixture;

    [Test]
    public async Task GetAllHidesDraftSessionsAndSessionsUnderHiddenParentEvents()
    {
        var marker = Guid.NewGuid().ToString("N");
        var visibleTitle = $"Visible Session {marker}";
        var draftSessionTitle = $"Draft Session {marker}";
        var draftParentTitle = $"Draft Parent Session {marker}";
        var privateParentTitle = $"Private Parent Session {marker}";

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            var visibleEvent = CreateEvent(seed, EventStatusEnum.Published, VisibilityTypeEnum.Public, $"Visible Event {marker}");
            var draftParentEvent = CreateEvent(seed, EventStatusEnum.Draft, VisibilityTypeEnum.Public, $"Draft Event {marker}");
            var privateParentEvent = CreateEvent(seed, EventStatusEnum.Published, VisibilityTypeEnum.Private, $"Private Event {marker}");

            context.Events.AddRange(visibleEvent, draftParentEvent, privateParentEvent);
            await context.SaveChangesAsync();

            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, visibleTitle, EventSessionStatusEnum.Published, 0));
            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, draftSessionTitle, EventSessionStatusEnum.Draft, 1));
            context.EventSessions.Add(CreateScheduledSession(draftParentEvent, seed.TenantId, draftParentTitle, EventSessionStatusEnum.Published, 2));
            context.EventSessions.Add(CreateScheduledSession(privateParentEvent, seed.TenantId, privateParentTitle, EventSessionStatusEnum.Published, 3));
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/eventsession?pageNumber=1&pageSize=50&visibilityMarker={marker}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(visibleTitle);
        await Assert.That(content).DoesNotContain(draftSessionTitle);
        await Assert.That(content).DoesNotContain(draftParentTitle);
        await Assert.That(content).DoesNotContain(privateParentTitle);
    }

    [Test]
    public async Task GetByEventReturnsOnlyPublishedSessionsForPublicParentEvent()
    {
        var marker = Guid.NewGuid().ToString("N");
        var visibleTitle = $"By Event Visible Session {marker}";
        var draftTitle = $"By Event Draft Session {marker}";
        Guid eventId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);
            var visibleEvent = CreateEvent(seed, EventStatusEnum.Published, VisibilityTypeEnum.Public, $"By Event Parent {marker}");
            eventId = visibleEvent.Id;

            context.Events.Add(visibleEvent);
            await context.SaveChangesAsync();

            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, visibleTitle, EventSessionStatusEnum.Published, 0));
            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, draftTitle, EventSessionStatusEnum.Draft, 1));
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/eventsession/by-event/{eventId}?visibilityMarker={marker}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(visibleTitle);
        await Assert.That(content).DoesNotContain(draftTitle);
    }

    [Test]
    public async Task GetManagedByEventReturnsDraftSessionsForAuthorizedInternalRead()
    {
        var marker = Guid.NewGuid().ToString("N");
        var visibleTitle = $"Managed Visible Session {marker}";
        var draftTitle = $"Managed Draft Session {marker}";
        Guid eventId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);
            var visibleEvent = CreateEvent(seed, EventStatusEnum.Published, VisibilityTypeEnum.Public, $"Managed Parent {marker}");
            eventId = visibleEvent.Id;

            context.Events.Add(visibleEvent);
            await context.SaveChangesAsync();

            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, visibleTitle, EventSessionStatusEnum.Published, 0));
            context.EventSessions.Add(CreateScheduledSession(visibleEvent, seed.TenantId, draftTitle, EventSessionStatusEnum.Draft, 1));
            await context.SaveChangesAsync();
        }

        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/eventsession/management/by-event/{eventId}?visibilityMarker={marker}");
        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(visibleTitle);
        await Assert.That(content).Contains(draftTitle);
    }

    [Test]
    public async Task GetManagedByEventDoesNotReturnSessionsFromAnotherTenant()
    {
        var marker = Guid.NewGuid().ToString("N");
        var hiddenTitle = $"Other Tenant Managed Session {marker}";
        var otherTenantId = Guid.NewGuid();
        Guid otherTenantEventId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureTenantActorAsync(
                context,
                otherTenantId,
                $"Other Tenant {marker}",
                $"other-tenant-{marker}");

            var otherTenantEvent = CreateEvent(
                seed,
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                $"Other Tenant Parent {marker}");
            otherTenantEventId = otherTenantEvent.Id;

            context.Events.Add(otherTenantEvent);
            await context.SaveChangesAsync();

            context.EventSessions.Add(CreateScheduledSession(
                otherTenantEvent,
                seed.TenantId,
                hiddenTitle,
                EventSessionStatusEnum.Draft,
                0));
            await context.SaveChangesAsync();
        }

        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/eventsession/management/by-event/{otherTenantEventId}?visibilityMarker={marker}");
        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).DoesNotContain(hiddenTitle);
    }

    [Test]
    public async Task GetByIdReturnsNotFoundForDraftSession()
    {
        var marker = Guid.NewGuid().ToString("N");
        Guid sessionId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);
            var visibleEvent = CreateEvent(seed, EventStatusEnum.Published, VisibilityTypeEnum.Public, $"Detail Parent {marker}");

            context.Events.Add(visibleEvent);
            await context.SaveChangesAsync();

            var session = CreateScheduledSession(visibleEvent, seed.TenantId, $"Detail Draft Session {marker}", EventSessionStatusEnum.Draft, 0);
            sessionId = session.Id;
            context.EventSessions.Add(session);
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/eventsession/{sessionId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetByIdReturnsSafeNotFoundForCrossTenantSession()
    {
        var marker = Guid.NewGuid().ToString("N");
        var hiddenTitle = $"Cross Tenant Detail Session {marker}";
        var otherTenantId = Guid.NewGuid();
        Guid sessionId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureTenantActorAsync(
                context,
                otherTenantId,
                $"Other Session Tenant {marker}",
                $"other-session-tenant-{marker}");
            var otherTenantEvent = CreateEvent(
                seed,
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                $"Cross Tenant Session Parent {marker}");

            context.Events.Add(otherTenantEvent);
            await context.SaveChangesAsync();

            var session = CreateScheduledSession(
                otherTenantEvent,
                seed.TenantId,
                hiddenTitle,
                EventSessionStatusEnum.Published,
                0);
            sessionId = session.Id;
            context.EventSessions.Add(session);
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/eventsession/{sessionId}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(content).DoesNotContain(hiddenTitle);
        await Assert.That(content).DoesNotContain(otherTenantId.ToString());
    }

    private static Explore.Domain.Event CreateEvent(
        DefaultTenantSeed seed,
        EventStatusEnum status,
        VisibilityTypeEnum visibility,
        string title) =>
        new EventBuilder()
            .WithTitle(title)
            .WithActorId(seed.ActorId)
            .WithTenantId(seed.TenantId)
            .WithStatus(status)
            .WithVisibility(visibility)
            .WithSessionDates(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)))
            .Build();

    private static EventSession CreateScheduledSession(
        Explore.Domain.Event parentEvent,
        Guid tenantId,
        string title,
        EventSessionStatusEnum status,
        int dayOffset)
    {
        var start = new DateTimeOffset(2026, 9, 1 + dayOffset, 9, 0, 0, TimeSpan.Zero);
        var session = new EventSession(status)
        {
            Id = Guid.NewGuid(),
            EventId = parentEvent.Id,
            Event = parentEvent,
            TenantId = tenantId,
            Tenant = null!,
            Title = title,
            SortOrder = dayOffset,
            ConcurrencyStamp = Guid.NewGuid()
        };

        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
        return session;
    }

    private static Task<DefaultTenantSeed> EnsureDefaultTenantActorAsync(ExploreDbContext context) =>
        EnsureTenantActorAsync(
            context,
            PlatformDefaults.DefaultTenantId,
            "Default Test Tenant",
            "default-test");

    private static async Task<DefaultTenantSeed> EnsureTenantActorAsync(
        ExploreDbContext context,
        Guid tenantId,
        string tenantFullName,
        string tenantSlug)
    {
        var tenant = await context.Tenants.FindAsync(tenantId);

        if (tenant is null)
        {
            tenant = new TenantBuilder()
                .WithId(tenantId)
                .WithFullName(tenantFullName)
                .WithSlug(tenantSlug)
                .Build();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var actor = await (
                from tenantUser in context.TenantUsers
                join candidate in context.Actors on tenantUser.UserId equals candidate.UserId
                where tenantUser.TenantId == tenantId && !tenantUser.IsDeleted && !candidate.IsDeleted
                select candidate)
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefaultAsync();

        if (actor is not null)
        {
            return new DefaultTenantSeed(tenantId, actor.Id);
        }

        var user = new UserBuilder().Build();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        actor = new ActorBuilder()
            .WithUserId(user.Id)
            .WithDisplayName("Default Session Visibility Actor")
            .Build();
        context.Actors.Add(actor);
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return new DefaultTenantSeed(tenantId, actor.Id);
    }

    private sealed record DefaultTenantSeed(Guid TenantId, Guid ActorId);
}
