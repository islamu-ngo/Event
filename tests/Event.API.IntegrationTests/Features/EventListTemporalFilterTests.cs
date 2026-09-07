using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public class EventListTemporalFilterTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    public EventListTemporalFilterTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestInstanceOperatorIdentityConfiguration.Apply(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddInMemoryExploreDbContext($"InMemoryDb_{Guid.NewGuid()}");

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<ITenantSlugCache>();
                services.AddSingleton<ITenantSlugCache>(new TestTenantSlugCache(_tenantId));
            });
            builder.ConfigureTestServices(services =>
            {
                TestHostServicePruner.RemoveNoisyHostedServices(services);
            });
        });
        _client = _factory.CreateClient();
    }

    private class TestTenantSlugCache : ITenantSlugCache
    {
        private readonly Guid _tenantId;
        public TestTenantSlugCache(Guid tenantId) => _tenantId = tenantId;
        public ValueTask<Guid?> GetTenantIdBySlugAsync(string slug, CancellationToken ct = default) => new(_tenantId);
        public ValueTask<Guid?> GetTenantIdByDomainAsync(string domain, CancellationToken ct = default) => new(_tenantId);
        public Task WarmAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task GetEvents_DefaultRequest_ExcludesPastEvents()
    {
        await SeedEventsAsync(PastSession(), UpcomingSession());

        var titles = await FetchEventTitlesAsync("/api/event");

        await Assert.That(titles.Count).IsEqualTo(1);
        await Assert.That(titles[0]).IsEqualTo("Upcoming Next Week");
    }

    [Test]
    public async Task GetEvents_ViewUpcomingAndOngoing_BlazorDefault_ExcludesPastEvents()
    {
        await SeedEventsAsync(PastSession(), UpcomingSession());

        var titles = await FetchEventTitlesAsync("/api/event?view=UpcomingAndOngoing");

        await Assert.That(titles.Count).IsEqualTo(1);
        await Assert.That(titles[0]).IsEqualTo("Upcoming Next Week");
    }

    [Test]
    public async Task GetEvents_ViewUpcomingAndOngoing_IncludesOngoingEvents()
    {
        await SeedEventsAsync(PastSession(), OngoingSession(), UpcomingSession());

        var titles = await FetchEventTitlesAsync("/api/event?view=UpcomingAndOngoing");

        await Assert.That(titles.Count).IsEqualTo(2);
        await Assert.That(titles).Contains("Ongoing Now");
        await Assert.That(titles).Contains("Upcoming Next Week");
    }

    [Test]
    public async Task GetEvents_ViewPast_ReturnsOnlyPastEvents()
    {
        await SeedEventsAsync(PastSession(), UpcomingSession());

        var titles = await FetchEventTitlesAsync("/api/event?view=Past");

        await Assert.That(titles.Count).IsEqualTo(1);
        await Assert.That(titles[0]).IsEqualTo("Past Days Ago");
    }

    [Test]
    public async Task GetEvents_ViewAll_ReturnsAllPublishedEvents()
    {
        await SeedEventsAsync(PastSession(), UpcomingSession());

        var titles = await FetchEventTitlesAsync("/api/event?view=All");

        await Assert.That(titles.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetEvents_WithExplicitDateRange_BypassesTemporalFilter_AndReturnsPastEvents()
    {
        await SeedEventsAsync(PastSessionDaysAgo(10));

        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var url = $"/api/event?dateFrom={dateFrom:O}&dateTo={dateTo:O}";

        var titles = await FetchEventTitlesAsync(url);

        await Assert.That(titles).Contains("Past Days Ago");
    }

    private static (string Title, DateTimeOffset Start, DateTimeOffset End) PastSession() =>
        ("Past Days Ago", DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-3).AddHours(2));

    private static (string Title, DateTimeOffset Start, DateTimeOffset End) PastSessionDaysAgo(int days) =>
        ("Past Days Ago", DateTimeOffset.UtcNow.AddDays(-days), DateTimeOffset.UtcNow.AddDays(-days).AddHours(2));

    private static (string Title, DateTimeOffset Start, DateTimeOffset End) OngoingSession() =>
        ("Ongoing Now", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(2));

    private static (string Title, DateTimeOffset Start, DateTimeOffset End) UpcomingSession() =>
        ("Upcoming Next Week", DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(7).AddHours(2));

    private async Task SeedEventsAsync(params (string Title, DateTimeOffset Start, DateTimeOffset End)[] sessions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await DatabaseSeeder.SeedAsync(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
            seedDevelopmentData: false);
        await EnsureTenantExistsAsync(dbContext);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"spec-{Guid.NewGuid():N}@example.com",
                FirstName = "Spec",
                LastName = "Tester"
            }
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Spec Test Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        dbContext.Actors.Add(actor);
        var tenant = await dbContext.Tenants.SingleAsync(candidate => candidate.Id == _tenantId);
        dbContext.TenantUsers.Add(new TenantUser
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
        await dbContext.SaveChangesAsync();

        foreach (var session in sessions)
        {
            var ev = new EventBuilder()
                .WithTitle(session.Title)
                .WithActorId(actor.Id)
                .WithTenantId(_tenantId)
                .WithStatus(EventStatusEnum.Published)
                .WithVisibility(VisibilityTypeEnum.Public)
                .WithSessionDates(
                    DateOnly.FromDateTime(session.Start.UtcDateTime),
                    DateOnly.FromDateTime(session.End.UtcDateTime))
                .Build();
            var es = CreateSession(_tenantId, ev, session.Start, session.End);
            ev.Sessions.Add(es);
            ev.RecalculateScheduleSummaryFromSessions();
            dbContext.Events.Add(ev);
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<List<string>> FetchEventTitlesAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant-Slug", "default");
        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {url} returned {response.StatusCode}. Body: {content}");
        }

        using var json = JsonDocument.Parse(content);
        return json.RootElement
            .GetProperty("_embedded")
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("event").GetProperty("title").GetString() ?? string.Empty)
            .ToList();
    }

    private async Task EnsureTenantExistsAsync(ExploreDbContext dbContext)
    {
        if (await dbContext.Tenants.AllAsync(t => t.Id != _tenantId))
        {
            dbContext.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                FullName = "Default Tenant",
                Slug = "default",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await dbContext.SaveChangesAsync();
        }
    }

    private static EventSession CreateSession(
        Guid tenantId,
        Explore.Domain.Event @event,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            LocationId = null,
            Location = null!,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            Title = @event.Title,
            SortOrder = 1,
            ConcurrencyStamp = Guid.NewGuid()
        };
        session.Reschedule(UtcInstantRange.Create(startsAt, endsAt), "UTC", new EventScheduleProjectionCalculator());
        return session;
    }
}
