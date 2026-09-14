using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Caching;
using Explore.Domain.Constants;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventAspectsHttpTests
{
    [Test]
    [Arguments("islamic")]
    [Arguments("tech")]
    public async Task PublicAbsenceAndPrivacy_ReturnDocumented404(string kind)
    {
        await using var factory = new NativeEventAspectsFactory();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var visible = await anonymous.GetAsync(Public(data.PublicId, kind));
        await Assert.That(visible.StatusCode).IsEqualTo(HttpStatusCode.OK);
        foreach (var id in new[] { data.PrivateId, data.DraftId, data.ForeignId, data.EmptyId, Guid.CreateVersion7() })
        {
            using var hidden = await anonymous.GetAsync(Public(id, kind));
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(hidden.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            var problem = await JsonAsync(hidden);
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(404);
            await Assert.That(problem.TryGetProperty("genderMode", out _)).IsFalse();
            await Assert.That(problem.TryGetProperty("githubRepoUrl", out _)).IsFalse();
        }
    }

    [Test]
    [Arguments("islamic")]
    [Arguments("tech")]
    public async Task ManagedPrivacyAndForgedWrites_FailClosedWithoutMutation(string kind)
    {
        await using var factory = new NativeEventAspectsFactory();
        var data = await SeedAsync(factory);
        using var owner = factory.Client(data.OwnerId);
        using var outsider = factory.Client(data.OutsiderId);
        using var anonymous = factory.CreateClient();
        var before = await GetAsync(owner, Managed(data.PrivateId, kind));
        using (var denied = await anonymous.GetAsync(Managed(data.PrivateId, kind)))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        foreach (var id in new[] { data.PrivateId, data.ForeignId, Guid.CreateVersion7() })
        {
            using var denied = await outsider.GetAsync(Managed(id, kind));
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        using (var foreign = await owner.GetAsync(Managed(data.ForeignId, kind)))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var unauthenticated = await anonymous.PostAsJsonAsync(Public(data.EmptyId, kind), CreateBody(kind)))
            await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        foreach (var target in new[] { data.PrivateId, data.ForeignId })
        {
            using var deniedCreate = await outsider.PostAsJsonAsync(Public(target, kind), CreateBody(kind));
            using var deniedPatch = await outsider.PatchAsJsonAsync(Public(target, kind), PatchBody(kind));
            using var forged = await outsider.PatchAsJsonAsync(Public(target, kind), new
            {
                eventId = data.EmptyId, tenantId = PlatformDefaults.DefaultTenantId, userId = data.OwnerId
            });
            await Assert.That(forged.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            using var deniedDelete = await outsider.DeleteAsync(Public(target, kind));
            await Assert.That(deniedCreate.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(deniedPatch.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(deniedDelete.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        await Assert.That(JsonElement.DeepEquals(before, await GetAsync(owner, Managed(data.PrivateId, kind)))).IsTrue();
    }

    [Test]
    [Arguments("islamic")]
    [Arguments("tech")]
    public async Task Writes_PreserveConflictGroupedPatchMissingAndCacheSemantics(string kind)
    {
        await using var factory = new NativeEventAspectsFactory();
        var data = await SeedAsync(factory);
        using var owner = factory.Client(data.OwnerId);
        var cache = factory.Services.GetRequiredService<HybridCache>();
        var detailKey = $"event:detail:{data.EmptyId}";
        var listKey = $"aspect-list:{data.EmptyId}";
        var tags = new[] { CacheTags.EventListByTenant(PlatformDefaults.DefaultTenantId) };
        await cache.SetAsync(detailKey, "before");
        await cache.SetAsync(listKey, "before", tags: tags);
        using (var created = await owner.PostAsJsonAsync(Public(data.EmptyId, kind), CreateBody(kind)))
        {
            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(created.Headers.Location!.AbsolutePath).IsEqualTo(Public(data.EmptyId, kind));
            await Assert.That((await JsonAsync(created)).GetProperty("id").GetGuid()).IsEqualTo(data.EmptyId);
        }
        await AssertEvictedAsync(cache, detailKey, listKey);
        var before = await GetAsync(owner, Managed(data.EmptyId, kind));
        using (var duplicate = await owner.PostAsJsonAsync(Public(data.EmptyId, kind), CreateBody(kind)))
            await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using (var invalid = await owner.PatchAsJsonAsync(Public(data.EmptyId, kind), new { }))
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await cache.SetAsync(detailKey, "before");
        await cache.SetAsync(listKey, "before", tags: tags);
        using (var changed = await owner.PatchAsJsonAsync(Public(data.EmptyId, kind), PatchBody(kind)))
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertEvictedAsync(cache, detailKey, listKey);
        var after = await GetAsync(owner, Managed(data.EmptyId, kind));
        if (kind == "islamic")
        {
            await Assert.That(after.TryGetProperty("referencePrayer", out _)).IsFalse();
            await Assert.That(after.TryGetProperty("prayerTimeOffset", out _)).IsFalse();
            await Assert.That(after.GetProperty("includesQuranRecitation").GetBoolean()).IsTrue();
            await Assert.That(after.GetProperty("genderMode").GetString()).IsEqualTo(before.GetProperty("genderMode").GetString());
        }
        else
        {
            await Assert.That(after.TryGetProperty("githubRepoUrl", out _)).IsFalse();
            await Assert.That(after.GetProperty("requiresLaptop").GetBoolean()).IsTrue();
            await Assert.That(after.GetProperty("hackathonTrack").GetString()).IsEqualTo("Tools");
        }
        await cache.SetAsync(detailKey, "before-delete");
        await cache.SetAsync(listKey, "before-delete", tags: tags);
        using (var deleted = await owner.DeleteAsync(Public(data.EmptyId, kind)))
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using (var repeated = await owner.DeleteAsync(Public(data.EmptyId, kind)))
            await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using (var missing = await owner.PatchAsJsonAsync(Public(data.EmptyId, kind), PatchBody(kind)))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        // Deletes intentionally retain the existing lack of HybridCache invalidation.
        await Assert.That(await cache.GetOrCreateAsync(detailKey, _ => ValueTask.FromResult("evicted"))).IsEqualTo("before-delete");
        await Assert.That(await cache.GetOrCreateAsync(listKey, _ => ValueTask.FromResult("evicted"))).IsEqualTo("before-delete");
    }

    private static async Task AssertEvictedAsync(HybridCache cache, string detailKey, string listKey)
    {
        await Assert.That(await cache.GetOrCreateAsync(detailKey, _ => ValueTask.FromResult("refreshed"))).IsEqualTo("refreshed");
        await Assert.That(await cache.GetOrCreateAsync(listKey, _ => ValueTask.FromResult("refreshed"))).IsEqualTo("refreshed");
    }

    private static string Public(Guid id, string kind) => $"/api/event/{id}/aspects/{kind}";
    private static string Managed(Guid id, string kind) => $"/api/event/{id}/management-aspects/{kind}";
    private static object CreateBody(string kind) => kind == "islamic"
        ? new { genderMode = 0, referencePrayer = 1, prayerTimeOffset = -15, includesQuranRecitation = true }
        : new { skillLevel = 0, githubRepoUrl = "https://code.example.test/project", hackathonTrack = "Tools", requiresLaptop = true };
    private static object PatchBody(string kind) => kind == "islamic"
        ? new { prayerSchedule = new { referencePrayer = new { hasValue = true, value = (int?)null }, prayerTimeOffset = new { hasValue = true, value = (int?)null } } }
        : new { repository = new { githubRepoUrl = new { hasValue = true, value = (string?)null } } };
    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
