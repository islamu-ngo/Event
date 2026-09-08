using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Event.Api.IntegrationTests.Seeds;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// Scenario-based HATEOAS tests for EventController on RealRuntime (PostgreSQL).
/// Seeds real data to verify item-level links (sessions, actor) and multi-page pagination
/// that are meaningless on an empty database.
/// </summary>
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public class EventHateoasScenarioTests(RealRuntimeApiFixture fixture)
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly RealRuntimeApiFixture _fixture = fixture;

    [Test]
    public async Task GetAll_WithSeededEvents_ShouldIncludeItemLinks()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedPublishedEventAsync(db, tenant.ActorId, tenant.TenantId);

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=20");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var embedded = json.RootElement.GetProperty("_embedded");
        var items = embedded.GetProperty("items");

        await Assert.That(items.GetArrayLength()).IsGreaterThanOrEqualTo(1);

        var firstItem = items[0];
        await Assert.That(firstItem.TryGetProperty("_links", out var itemLinks)).IsTrue();
        await Assert.That(itemLinks.TryGetProperty("self", out _)).IsTrue();
    }

    [Test]
    public async Task GetByPublicCode_WithSeededPublishedEvent_ShouldReturnEventDetails()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var seededEvent = await EventScenarioSeed.SeedPublishedEventAsync(
            db,
            tenant.ActorId,
            tenant.TenantId,
            "Public Slug Code Test");
        var slugCode = $"public-slug-code-test-{seededEvent.PublicCode}";

        var response = await _fixture.Client.GetAsync($"/api/event/public/{slugCode}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("title").GetString()).IsEqualTo(seededEvent.Title);
        await Assert.That(json.RootElement.GetProperty("publicCode").GetString()).IsEqualTo(seededEvent.PublicCode);
    }

    [Test]
    public async Task GetOpenGraphImage_WithSeededPublishedEvent_ReturnsPngAndStrongEtag()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var seededEvent = await EventScenarioSeed.SeedPublishedEventAsync(
            db,
            tenant.ActorId,
            tenant.TenantId,
            "Public Open Graph Image Test");
        var slugCode = $"public-open-graph-image-test-{seededEvent.PublicCode}";

        using var response = await _fixture.Client.GetAsync($"/api/event/public/{slugCode}/og-image");
        var pngBytes = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("image/png");
        await Assert.That(response.Headers.CacheControl?.Public).IsTrue();
        await Assert.That(response.Headers.CacheControl?.MustRevalidate).IsTrue();
        await Assert.That(response.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.Zero);
        await Assert.That(response.Headers.Vary.ToString()).IsEqualTo("Host, X-Tenant-Slug");
        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Headers.ETag!.IsWeak).IsFalse();
        await Assert.That(pngBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)).IsTrue();
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(16, 4))).IsEqualTo(1200);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(20, 4))).IsEqualTo(630);
    }

    [Test]
    public async Task GetOpenGraphImage_WithMatchingEtag_ReturnsNotModified()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var seededEvent = await EventScenarioSeed.SeedPublishedEventAsync(
            db,
            tenant.ActorId,
            tenant.TenantId,
            "Conditional Open Graph Image Test");
        var slugCode = $"conditional-open-graph-image-test-{seededEvent.PublicCode}";
        var path = $"/api/event/public/{slugCode}/og-image";

        using var firstResponse = await _fixture.Client.GetAsync(path);
        var etag = firstResponse.Headers.ETag?.ToString();
        ArgumentNullException.ThrowIfNull(etag);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var secondResponse = await _fixture.Client.SendAsync(request);
        var secondBody = await secondResponse.Content.ReadAsByteArrayAsync();

        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That(secondResponse.Headers.ETag?.ToString()).IsEqualTo(etag);
        await Assert.That(secondResponse.Headers.CacheControl?.Public).IsTrue();
        await Assert.That(secondResponse.Headers.CacheControl?.MustRevalidate).IsTrue();
        await Assert.That(secondResponse.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.Zero);
        await Assert.That(secondResponse.Headers.Vary.ToString()).IsEqualTo("Host, X-Tenant-Slug");
        await Assert.That(secondBody).IsEmpty();
    }

    [Test]
    public async Task GetOpenGraphImage_WithUnknownSlug_ReturnsGenericNotFound()
    {
        await _fixture.ResetDatabaseAsync();

        using var response = await _fixture.Client.GetAsync(
            "/api/event/public/missing-open-graph-image-test/og-image");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.NotFound,
            "Event not found");
    }

    [Test]
    public async Task GetAll_SeededEventItem_ShouldIncludeSessionsLink()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedPublishedEventAsync(db, tenant.ActorId, tenant.TenantId);

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=20");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("_embedded").GetProperty("items");

        await Assert.That(items.GetArrayLength()).IsGreaterThanOrEqualTo(1);

        var firstItemLinks = items[0].GetProperty("_links");
        if (firstItemLinks.TryGetProperty("sessions", out var sessionsLink))
        {
            var href = sessionsLink.GetProperty("href").GetString();
            await Assert.That(href).Contains("/api/eventsession/by-event/");
        }
    }

    [Test]
    public async Task GetAll_SeededEventItem_ShouldIncludeActorLink()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedPublishedEventAsync(db, tenant.ActorId, tenant.TenantId);

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=20");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("_embedded").GetProperty("items");

        await Assert.That(items.GetArrayLength()).IsGreaterThanOrEqualTo(1);

        var firstItemLinks = items[0].GetProperty("_links");
        if (firstItemLinks.TryGetProperty("actor", out var actorLink))
        {
            var href = actorLink.GetProperty("href").GetString();
            await Assert.That(href).Contains("/api/actor/");
        }
    }

    [Test]
    public async Task GetAll_WithMultipleSeededEvents_ShouldShowCorrectTotalCount()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedMultiplePublishedEventsAsync(db, tenant.ActorId, tenant.TenantId, 5);

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=20");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var totalCount = json.RootElement.GetProperty("totalCount").GetInt32();
        await Assert.That(totalCount).IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async Task GetAll_PaginatedSeededEvents_ShouldHaveNextLink_WhenMorePagesExist()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedMultiplePublishedEventsAsync(db, tenant.ActorId, tenant.TenantId, 6);

        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=3");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var links = json.RootElement.GetProperty("_links");

        await Assert.That(links.TryGetProperty("next", out var nextLink)).IsTrue();
        var nextHref = nextLink.GetProperty("href").GetString();
        await Assert.That(nextHref).Contains("pageNumber=2");
    }

    [Test]
    public async Task GetAll_WithPreferMinimal_SeededEvents_ItemsShouldNotHaveLinks()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        await EventScenarioSeed.SeedPublishedEventAsync(db, tenant.ActorId, tenant.TenantId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/event?pageNumber=1&pageSize=20");
        request.Headers.Add("Prefer", "return=minimal");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("_embedded").GetProperty("items");

        await Assert.That(items.GetArrayLength()).IsGreaterThanOrEqualTo(1);

        var hasLinks = items[0].TryGetProperty("_links", out var linksElement);
        if (hasLinks)
        {
            await Assert.That(linksElement.ValueKind).IsEqualTo(JsonValueKind.Object);
            await Assert.That(linksElement.EnumerateObject().Any()).IsFalse();
        }
    }
}
