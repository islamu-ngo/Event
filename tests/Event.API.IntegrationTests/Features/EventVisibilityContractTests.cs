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

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("ContractEventVisibility")]
public class EventVisibilityContractTests(ContractApiFixture fixture)
{
    private readonly ContractApiFixture _fixture = fixture;

    [Test]
    public async Task GetAllWithoutStatusFilterHidesDraftArchivedAndModeratedEvents()
    {
        var marker = Guid.NewGuid().ToString("N");
        var publishedTitle = $"Published Visibility Event {marker}";
        var draftTitle = $"Draft Visibility Event {marker}";
        var archivedTitle = $"Archived Visibility Event {marker}";
        var moderatedTitle = $"Moderated Visibility Event {marker}";

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            AddEvent(context, seed.TenantId, seed.ActorId, publishedTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.Public, includePublishedSession: true);

            AddEvent(context, seed.TenantId, seed.ActorId, draftTitle,
                EventStatusEnum.Draft, VisibilityTypeEnum.Public, includePublishedSession: false);

            AddEvent(context, seed.TenantId, seed.ActorId, archivedTitle,
                EventStatusEnum.Archived, VisibilityTypeEnum.Public, includePublishedSession: false);

            AddEvent(context, seed.TenantId, seed.ActorId, moderatedTitle,
                EventStatusEnum.Moderated, VisibilityTypeEnum.Public, includePublishedSession: false);

            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=50");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(publishedTitle);
        await Assert.That(content).DoesNotContain(draftTitle);
        await Assert.That(content).DoesNotContain(archivedTitle);
        await Assert.That(content).DoesNotContain(moderatedTitle);
    }

    [Test]
    public async Task GetAllWithExplicitModeratedStatusFilterDoesNotExposeModeratedEvents()
    {
        var marker = Guid.NewGuid().ToString("N");
        var moderatedTitle = $"Explicit Moderated Visibility Event {marker}";

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            context.Events.Add(new EventBuilder()
                .WithTitle(moderatedTitle)
                .WithActorId(seed.ActorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Moderated)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());

            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/event?pageNumber=1&pageSize=50&eventStatusIds={(int)EventStatusEnum.Moderated}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).DoesNotContain(moderatedTitle);
    }

    [Test]
    public async Task GetAllWithActorIdFiltersByActorAndKeepsPublicDiscoverability()
    {
        var marker = Guid.NewGuid().ToString("N");
        var matchingTitle = $"Actor Matching Published {marker}";
        var otherActorTitle = $"Actor Other Published {marker}";
        var hiddenTitle = $"Actor Matching Draft {marker}";
        var privateTitle = $"Actor Matching Private {marker}";
        var membersOnlyTitle = $"Actor Matching Members Only {marker}";
        var crossTenantTitle = $"Actor Cross Tenant Published {marker}";
        Guid actorId;
        Guid crossTenantActorId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            var user = new UserBuilder().Build();
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var actor = new ActorBuilder()
                .WithUserId(user.Id)
                .WithDisplayName("Actor Filter Owner")
                .Build();
            context.Actors.Add(actor);
            var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == seed.TenantId);
            AddActiveTenantUser(context, tenant, user, actor);
            await context.SaveChangesAsync();
            actorId = actor.Id;

            var crossTenant = new TenantBuilder()
                .WithFullName($"Cross Tenant {marker}")
                .WithSlug($"cross-tenant-{marker}")
                .Build();
            var crossTenantUser = new UserBuilder().Build();
            context.Tenants.Add(crossTenant);
            context.Users.Add(crossTenantUser);
            await context.SaveChangesAsync();

            var crossTenantActor = new ActorBuilder()
                .WithUserId(crossTenantUser.Id)
                .WithDisplayName("Cross Tenant Actor Filter Owner")
                .Build();
            context.Actors.Add(crossTenantActor);
            AddActiveTenantUser(context, crossTenant, crossTenantUser, crossTenantActor);
            await context.SaveChangesAsync();
            crossTenantActorId = crossTenantActor.Id;

            AddEvent(context, seed.TenantId, actorId, matchingTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.Public, includePublishedSession: true);

            AddEvent(context, seed.TenantId, actorId, hiddenTitle,
                EventStatusEnum.Draft, VisibilityTypeEnum.Public, includePublishedSession: false);

            AddEvent(context, seed.TenantId, actorId, privateTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.Private, includePublishedSession: true);

            AddEvent(context, seed.TenantId, actorId, membersOnlyTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.MembersOnly, includePublishedSession: true);

            AddEvent(context, seed.TenantId, seed.ActorId, otherActorTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.Public, includePublishedSession: true);

            AddEvent(context, crossTenant.Id, crossTenantActorId, crossTenantTitle,
                EventStatusEnum.Published, VisibilityTypeEnum.Public, includePublishedSession: true);

            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/event?pageNumber=1&pageSize=50&actorId={actorId}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(matchingTitle);
        await Assert.That(content).DoesNotContain(otherActorTitle);
        await Assert.That(content).DoesNotContain(hiddenTitle);
        await Assert.That(content).DoesNotContain(privateTitle);
        await Assert.That(content).DoesNotContain(membersOnlyTitle);
        await Assert.That(content).DoesNotContain(crossTenantTitle);

        var crossTenantResponse = await _fixture.Client.GetAsync($"/api/event?pageNumber=1&pageSize=50&actorId={crossTenantActorId}");
        var crossTenantContent = await crossTenantResponse.Content.ReadAsStringAsync();

        await Assert.That(crossTenantResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(crossTenantContent).DoesNotContain(crossTenantTitle);
    }

    [Test]
    public async Task GetManagedByActorReturnsAuthorizedHiddenEventsWithoutOtherActors()
    {
        var marker = Guid.NewGuid().ToString("N");
        var publishedTitle = $"Managed Actor Published {marker}";
        var draftTitle = $"Managed Actor Draft {marker}";
        var moderatedTitle = $"Managed Actor Moderated {marker}";
        var otherActorTitle = $"Managed Other Actor Moderated {marker}";
        Guid actorId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            var owner = new UserBuilder().Build();
            context.Users.Add(owner);
            await context.SaveChangesAsync();

            var actor = new ActorBuilder()
                .WithUserId(owner.Id)
                .WithDisplayName("Managed Profile Actor")
                .Build();
            context.Actors.Add(actor);
            var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == seed.TenantId);
            AddActiveTenantUser(context, tenant, owner, actor);
            await context.SaveChangesAsync();
            actorId = actor.Id;

            context.Events.Add(new EventBuilder()
                .WithTitle(publishedTitle)
                .WithActorId(actorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Published)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());

            context.Events.Add(new EventBuilder()
                .WithTitle(draftTitle)
                .WithActorId(actorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Draft)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());

            context.Events.Add(new EventBuilder()
                .WithTitle(moderatedTitle)
                .WithActorId(actorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Moderated)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());

            context.Events.Add(new EventBuilder()
                .WithTitle(otherActorTitle)
                .WithActorId(seed.ActorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Moderated)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());

            await context.SaveChangesAsync();
        }

        var anonymousResponse = await _fixture.Client.GetAsync($"/api/event/management/by-actor/{actorId}?pageNumber=1&pageSize=50");
        await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/event/management/by-actor/{actorId}?pageNumber=1&pageSize=50");

        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(publishedTitle);
        await Assert.That(content).Contains(draftTitle);
        await Assert.That(content).Contains(moderatedTitle);
        await Assert.That(content).DoesNotContain(otherActorTitle);
    }

    [Test]
    public async Task GetByIdForDraftEventReturnsNotFoundForAnonymousUser()
    {
        var eventId = await SeedHiddenEventAsync(EventStatusEnum.Draft);

        var response = await _fixture.Client.GetAsync($"/api/event/{eventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetByIdForArchivedEventReturnsNotFound()
    {
        var eventId = await SeedHiddenEventAsync(EventStatusEnum.Archived);

        var response = await _fixture.Client.GetAsync($"/api/event/{eventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetByIdForModeratedEventReturnsNotFoundForAnonymousUser()
    {
        var eventId = await SeedHiddenEventAsync(EventStatusEnum.Moderated);

        var response = await _fixture.Client.GetAsync($"/api/event/{eventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetByPublicCodeForModeratedEventReturnsSafeNotFound()
    {
        var marker = Guid.NewGuid().ToString("N");
        var publicCode = marker;
        var title = $"Moderated Public Code Event {marker}";

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureDefaultTenantActorAsync(context);

            context.Events.Add(new EventBuilder()
                .WithTitle(title)
                .WithPublicCode(publicCode)
                .WithActorId(seed.ActorId)
                .WithTenantId(seed.TenantId)
                .WithStatus(EventStatusEnum.Moderated)
                .WithVisibility(VisibilityTypeEnum.Public)
                .Build());
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/event/public/event-{publicCode}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(content).DoesNotContain(title);
    }

    [Test]
    public async Task GetByIdForCrossTenantEventReturnsSafeNotFound()
    {
        var marker = Guid.NewGuid().ToString("N");
        var title = $"Cross Tenant Detail Event {marker}";
        var otherTenantId = Guid.NewGuid();
        Guid eventId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await EnsureTenantActorAsync(
                context,
                otherTenantId,
                $"Other Detail Tenant {marker}",
                $"other-detail-tenant-{marker}");

            var hiddenEvent = AddEvent(
                context,
                seed.TenantId,
                seed.ActorId,
                title,
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                includePublishedSession: true);
            eventId = hiddenEvent.Id;
            await context.SaveChangesAsync();
        }

        var response = await _fixture.Client.GetAsync($"/api/event/{eventId}");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(content).DoesNotContain(title);
        await Assert.That(content).DoesNotContain(otherTenantId.ToString());
    }

    [Test]
    public async Task GetManagementDetailsForModeratedEventReturnsUnauthorizedForAnonymousUser()
    {
        var eventId = await SeedHiddenEventAsync(EventStatusEnum.Moderated);

        var response = await _fixture.Client.GetAsync($"/api/event/{eventId}/management-detail");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetManagementDetailsForModeratedEventReturnsEventForAuthorizedUser()
    {
        var marker = Guid.NewGuid().ToString("N");
        var title = $"Authorized Moderated Management Event {marker}";
        var eventId = await SeedHiddenEventAsync(EventStatusEnum.Moderated, title);
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/event/{eventId}/management-detail");

        var response = await _fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains(title);
    }

    private async Task<Guid> SeedHiddenEventAsync(EventStatusEnum status, string? title = null)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var seed = await EnsureDefaultTenantActorAsync(context);

        var hiddenEvent = new EventBuilder()
            .WithTitle(title ?? $"{status} Hidden Event {Guid.NewGuid():N}")
            .WithActorId(seed.ActorId)
            .WithTenantId(seed.TenantId)
            .WithStatus(status)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();

        context.Events.Add(hiddenEvent);
        await context.SaveChangesAsync();
        return hiddenEvent.Id;
    }

    private static Explore.Domain.Event AddEvent(
        ExploreDbContext context,
        Guid tenantId,
        Guid actorId,
        string title,
        EventStatusEnum status,
        VisibilityTypeEnum visibility,
        bool includePublishedSession)
    {
        var sessionStart = DateTimeOffset.UtcNow.AddDays(7);
        var @event = new EventBuilder()
            .WithTitle(title)
            .WithActorId(actorId)
            .WithTenantId(tenantId)
            .WithStatus(status)
            .WithVisibility(visibility)
            .WithSessionDates(
                DateOnly.FromDateTime(sessionStart.UtcDateTime),
                DateOnly.FromDateTime(sessionStart.UtcDateTime))
            .Build();

        context.Events.Add(@event);

        if (includePublishedSession)
        {
            var session = CreatePublishedSession(@event, tenantId, sessionStart);
            @event.Sessions.Add(session);
            @event.RecalculateScheduleSummaryFromSessions();
            context.EventSessions.Add(session);
        }

        return @event;
    }

    private static EventSession CreatePublishedSession(
        Explore.Domain.Event @event,
        Guid tenantId,
        DateTimeOffset startUtc)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            Title = @event.Title,
            SortOrder = 1,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.NewGuid()
        };

        session.Reschedule(UtcInstantRange.Create(startUtc, startUtc.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
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
            .WithDisplayName("Default Visibility Actor")
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

    private static void AddActiveTenantUser(ExploreDbContext context, Tenant tenant, User user, Actor actor)
    {
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
    }
}
