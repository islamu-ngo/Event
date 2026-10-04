using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventDiscoveryTraversalTests
{
    [Test]
    public async Task ExpiredCursorReturnsGoneAtControlledDeadlineWithoutMembershipMetadata()
    {
        var clock = new TraversalClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new NativeEventTagsFactory(relational: true);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        var seed = await SeedAsync(host);
        using var first = await client.GetAsync(Route(seed.Title));
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        string next = json.RootElement.GetProperty("_links").GetProperty("next").GetProperty("href").GetString()!;
        clock.Now = json.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        using var expired = await client.GetAsync(next);
        await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.Gone);
        await AssertFailureAsync(expired, "discovery_cursor_expired");
    }

    [Test]
    public async Task RankingMutationDoesNotReorderCapturedContinuationMembership()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        Guid[] expected;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            expected = await context.Events.Where(value => value.Title.StartsWith(seed.Title))
                .OrderBy(value => value.Title).Select(value => value.Id).ToArrayAsync();
        }
        string firstRoute = $"/api/Event?searchTerm={seed.Title}&pageSize=2&sortBy=views&sortDescending=true";
        using var first = await client.GetAsync(firstRoute);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        await Assert.That(ItemIds(firstJson.RootElement).SequenceEqual(expected.Take(2))).IsTrue();
        string? next = firstJson.RootElement.GetProperty("_links").GetProperty("next").GetProperty("href").GetString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var events = await context.Events.Where(value => value.Title.StartsWith(seed.Title))
                .OrderBy(value => value.Title).ToArrayAsync();
            for (int index = 0; index < events.Length; index++)
                events[index].TotalViews = index * 100;
            await context.SaveChangesAsync();
        }
        var returned = ItemIds(firstJson.RootElement).ToList();
        for (int page = 0; next is not null && page < 3; page++)
        {
            using var response = await client.GetAsync(next);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            returned.AddRange(ItemIds(json.RootElement));
            next = NextLink(json.RootElement);
        }
        await Assert.That(next).IsNull();
        await Assert.That(returned.SequenceEqual(expected)).IsTrue();
        // Different matching criteria capture the current ranking rather than reusing the old snapshot.
        using var recaptured = await client.GetAsync(firstRoute + "&formatIds=" + (int)EventFormatEnum.Digital);
        await Assert.That(recaptured.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var recapturedJson = JsonDocument.Parse(await recaptured.Content.ReadAsStringAsync());
        await Assert.That(ItemIds(recapturedJson.RootElement).SequenceEqual(expected.Reverse().Take(2))).IsTrue();
    }

    [Test]
    public async Task MoreThanOneThousandCandidatesTruncatesAndTerminatesAtStoredBound()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, 1001);
        string? route = $"/api/Event?searchTerm={seed.Title}&pageSize=100&sortBy=title&sortDescending=false";
        var ids = new HashSet<Guid>();
        int pages = 0;
        while (route is not null && pages < 11)
        {
            using var response = await client.GetAsync(route);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var body = json.RootElement;
            await Assert.That(body.GetProperty("snapshotCount").GetInt32()).IsEqualTo(1000);
            await Assert.That(body.GetProperty("truncated").GetBoolean()).IsTrue();
            await Assert.That(body.TryGetProperty("totalCount", out _)).IsFalse();
            var pageIds = ItemIds(body);
            await Assert.That(pageIds.Length).IsEqualTo(100);
            foreach (var id in pageIds)
                await Assert.That(ids.Add(id)).IsTrue();
            route = NextLink(body);
            await Assert.That(body.GetProperty("hasMore").GetBoolean()).IsEqualTo(route is not null);
            await Assert.That(body.GetProperty("_links").TryGetProperty("last", out _)).IsFalse();
            pages++;
        }
        await Assert.That(route).IsNull();
        await Assert.That(pages).IsEqualTo(10);
        await Assert.That(ids.Count).IsEqualTo(1000);
    }

    [Test]
    public async Task CursorFromAnotherTenantCannotReleaseMembership()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string next = await NextAsync(client, Route(seed.Title));
        Guid otherTenantId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            otherTenantId = (await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context)).TenantId;
        }
        await using var otherHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Deployment:DefaultTenantId"] = otherTenantId.ToString()
                })));
        using var otherClient = otherHost.CreateClient();
        using var ownSearch = await otherClient.GetAsync(Route(seed.Title));
        await Assert.That(ownSearch.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var ownJson = JsonDocument.Parse(await ownSearch.Content.ReadAsStringAsync());
        await Assert.That(ownJson.RootElement.GetProperty("snapshotCount").GetInt32()).IsEqualTo(0);
        await Assert.That(ItemIds(ownJson.RootElement).Length).IsEqualTo(0);
        using var rejected = await otherClient.GetAsync(next);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertFailureAsync(rejected, "discovery_cursor_invalid");
    }

    [Test]
    public async Task PrivateTenantFirstSearchIsConcealedWithoutHistoricalMembership()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        await PrivatizeAsync(factory);
        using var response = await client.GetAsync(Route(seed.Title));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertFailureAsync(response, "tenant_lifecycle_unavailable");
    }

    [Test]
    public async Task PublicCursorCannotReleaseMembershipAfterTenantBecomesPrivate()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string next = await NextAsync(client, Route(seed.Title));
        await PrivatizeAsync(factory);
        using var response = await client.GetAsync(next);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertFailureAsync(response, "tenant_lifecycle_unavailable");
    }

    [Test]
    public async Task ForwardHalTraversalIsUniqueAndReportsStoredMembershipNotLivePages()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string? route = Route(seed.Title);
        var returned = new HashSet<Guid>();
        int pages = 0;
        while (route is not null && pages < 4)
        {
            using var response = await client.GetAsync(route);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var body = json.RootElement;
            await Assert.That(body.GetProperty("snapshotCount").GetInt32()).IsEqualTo(6);
            await Assert.That(body.TryGetProperty("totalPages", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("totalCount", out _)).IsFalse();
            foreach (var item in body.GetProperty("_embedded").GetProperty("items").EnumerateArray())
                await Assert.That(returned.Add(item.GetProperty("event").GetProperty("id").GetGuid())).IsTrue();
            var links = body.GetProperty("_links");
            route = links.TryGetProperty("next", out var next) ? next.GetProperty("href").GetString() : null;
            await Assert.That(body.GetProperty("hasMore").GetBoolean()).IsEqualTo(route is not null);
            await Assert.That(links.TryGetProperty("last", out _)).IsFalse();
            pages++;
        }
        await Assert.That(pages).IsEqualTo(3);
        await Assert.That(returned.Count).IsEqualTo(6);
        await Assert.That(route).IsNull();
    }

    [Test]
    public async Task ChangingOriginalCriteriaRejectsContinuationWithoutHistoricalMetadata()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string next = await NextAsync(client, Route(seed.Title));
        using var response = await client.GetAsync(next + "&areaId=" + Guid.CreateVersion7());
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertFailureAsync(response, "discovery_cursor_invalid");
    }

    [Test]
    public async Task RemovedMatchingSessionInvalidatesOldTraversalButFreshSearchOmitsIt()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string next = await NextAsync(client, Route(seed.Title));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var session = await context.EventSessions.SingleAsync(value => value.Id == seed.LastSessionId);
            session.IsDeleted = true;
            await context.SaveChangesAsync();
        }
        using var response = await client.GetAsync(next);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertFailureAsync(response, "discovery_restart_required");
        using var fresh = await client.GetAsync(Route(seed.Title));
        await Assert.That(fresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("snapshotCount").GetInt32()).IsEqualTo(5);
    }

    [Test]
    public async Task TamperedCursorCannotReleaseMembershipOrCounts()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory);
        string next = await NextAsync(client, Route(seed.Title));
        int start = next.IndexOf("cursor=", StringComparison.Ordinal) + "cursor=".Length;
        int index = start + 20;
        string tampered = next[..index] + (next[index] == 'A' ? 'B' : 'A') + next[(index + 1)..];
        using var response = await client.GetAsync(tampered);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertFailureAsync(response, "discovery_cursor_invalid");
    }

    private static string Route(string title) =>
        $"/api/Event?searchTerm={Uri.EscapeDataString(title)}&pageSize=2&sortBy=title&sortDescending=false";

    private static async Task<string> NextAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("_links").GetProperty("next").GetProperty("href").GetString()!;
    }

    private static async Task AssertFailureAsync(HttpResponseMessage response, string code)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
        await Assert.That(json.RootElement.TryGetProperty("snapshotCount", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("truncated", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("_embedded", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("expiresAt", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("hasMore", out _)).IsFalse();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    private static Guid[] ItemIds(JsonElement body) => body.GetProperty("_embedded").GetProperty("items")
        .EnumerateArray().Select(item => item.GetProperty("event").GetProperty("id").GetGuid()).ToArray();

    private static string? NextLink(JsonElement body) =>
        body.GetProperty("_links").TryGetProperty("next", out var next) ? next.GetProperty("href").GetString() : null;

    private static async Task PrivatizeAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await context.Tenants.SingleAsync(value => value.Id == PlatformDefaults.DefaultTenantId);
        tenant.TenantStatusId = (int)TenantStatusEnum.Provisioning;
        await context.SaveChangesAsync();
    }

    private sealed class TraversalClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }


    internal static async Task<(string Title, Guid LastSessionId)> SeedAsync(WebApplicationFactory<Program> factory, int count = 6)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        string title = $"traversal-{Guid.CreateVersion7():N}";
        Guid lastSessionId = Guid.Empty;
        for (int index = 0; index < count; index++)
        {
            var entity = new EventBuilder().WithId(Guid.CreateVersion7()).WithTitle($"{title}-{index:D4}")
                .WithActorId(tenant.ActorId).WithTenantId(tenant.TenantId)
                .WithStatus(EventStatusEnum.Published).WithFormat(EventFormatEnum.Digital).Build();
            entity.TotalViews = count - index;
            var start = new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), EventId = entity.Id, Event = entity, TenantId = tenant.TenantId,
                Tenant = null!, StartTime = start, EndTime = start.AddHours(1)
            };
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            entity.Sessions.Add(session);
            entity.RecalculateScheduleSummaryFromSessions();
            context.Events.Add(entity);
            lastSessionId = session.Id;
        }
        await context.SaveChangesAsync();
        return (title, lastSessionId);
    }
}
