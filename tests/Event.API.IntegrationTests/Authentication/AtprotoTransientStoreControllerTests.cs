// ABOUTME: Exercises private create/read/consume through real controllers, MediatR and P1 PostgreSQL stores.
// ABOUTME: Proves tenant binding, collision, expiry, health-purpose exclusion and no generic response replay.

using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientStoreControllerTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    [Arguments("oauth_state")]
    [Arguments("tenant_handoff")]
    public async Task RoundTrip_RequiresTenantAndSingleWinner_DespiteIdempotencyAndCacheHeaders(string purpose)
    {
        Guid tenant = await fixture.SeedTenantAsync();
        Guid other = await fixture.SeedTenantAsync();
        string digest = AtprotoTransientApiFixture.NewDigest();
        string payload = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(128));
        byte[] create = CreateBody(purpose, digest, tenant, payload);
        using var created = await SendAsync("create", create);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var value = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid id = value.RootElement.GetProperty("id").GetGuid();
        await Assert.That(value.RootElement.GetProperty("protectedPayload").GetString()).IsEqualTo(payload);
        using var conflict = await SendAsync("create", create);
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var wrongTenant = await SendAsync("read", fixture.ReadBody(digest, purpose, other));
        await Assert.That(wrongTenant.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var tenantFree = await SendAsync("read", fixture.ReadBody(digest, purpose));
        await Assert.That(tenantFree.StatusCode).IsEqualTo(purpose == "oauth_state" ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        using var read = await SendAsync("read", fixture.ReadBody(digest, purpose, tenant));
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        byte[] wrongConsume = ConsumeBody(id, purpose, digest, other);
        using var rejected = await SendAsync("consume", wrongConsume);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        byte[] consume = ConsumeBody(id, purpose, digest, tenant);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpStatusCode>[] contenders = Enumerable.Range(0, 2).Select(async _ =>
        {
            await start.Task.WaitAsync(deadline.Token);
            using var response = await SendAsync("consume", consume, deadline.Token);
            return response.StatusCode;
        }).ToArray();
        start.SetResult();
        HttpStatusCode[] statuses = await Task.WhenAll(contenders);
        await Assert.That(statuses.Count(status => status == HttpStatusCode.OK)).IsEqualTo(1);
        await Assert.That(statuses.Count(status => status == HttpStatusCode.NotFound)).IsEqualTo(1);
        using var absent = await SendAsync("read", fixture.ReadBody(digest, purpose, tenant));
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var replay = await SendAsync("consume", consume);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisabledOrMissingTenant_AndInvalidLifetimes_CannotCreate()
    {
        Guid disabled = await fixture.SeedTenantAsync(enabled: false);
        foreach (Guid tenant in new[] { disabled, Guid.CreateVersion7() })
        {
            using var response = await SendAsync("create", CreateBody("oauth_state", AtprotoTransientApiFixture.NewDigest(), tenant, "opaque"));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        Guid enabled = await fixture.SeedTenantAsync();
        foreach (var spec in new[] { ("oauth_state", 601), ("tenant_handoff", 121), ("oauth_state", 0), ("tenant_handoff", -1) })
        {
            byte[] body = CreateBody(spec.Item1, AtprotoTransientApiFixture.NewDigest(), enabled, "opaque", spec.Item2);
            using var response = await SendAsync("create", body);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task ExpiredAndDisabledRows_AreNotFound_AndNeverConsumed()
    {
        Guid disabled = await fixture.SeedTenantAsync(enabled: false);
        Guid enabled = await fixture.SeedTenantAsync();
        foreach (var spec in new[] { (disabled, 30), (enabled, -1) })
        {
            string digest = AtprotoTransientApiFixture.NewDigest();
            var row = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, digest, spec.Item1, "opaque",
                fixture.Clock.GetUtcNow().AddSeconds(spec.Item2).ToUnixTimeMilliseconds());
            // P1 accepts immutable timestamps; Application/HTTP owns the creation TTL ceiling.
            if (spec.Item2 < 0)
            {
                await using var scope = fixture.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
                db.AtprotoTransientRecords.Add(row);
                await db.SaveChangesAsync();
            }
            else
            {
                await using var scope = fixture.Factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAtprotoTransientStoreRepository>().TryCreateAsync(row);
            }
            using var read = await SendAsync("read", fixture.ReadBody(digest));
            using var consume = await SendAsync("consume", ConsumeBody(row.Id, "oauth_state", digest, spec.Item1));
            await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(consume.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task HealthProbePurpose_IsExcludedFromEveryOrdinaryRoute()
    {
        foreach (string operation in new[] { "create", "read", "consume" })
        {
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { purpose = "health_probe", tokenDigest = AtprotoTransientApiFixture.NewDigest() });
            using var response = await SendAsync(operation, body);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task PublicSpecifications_ContainNeitherPrivateRoutesNorModels()
    {
        foreach (string path in new[] { "/openapi/islamu-event.json", "/swagger/v0.1/swagger.json" })
        {
            using var response = await fixture.Client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(document.RootElement.GetProperty("paths").EnumerateObject()
                .Any(route => route.Name.StartsWith(AtprotoTransientApiFixture.Prefix, StringComparison.OrdinalIgnoreCase))).IsFalse();
            await Assert.That(document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
                .Any(schema => schema.Name.Contains("AtprotoTransient", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task PublishedHalLinks_DoNotAdvertisePrivateTransientOperations()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event?pageSize=1");
        request.Headers.Accept.ParseAdd("application/hal+json");
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement links = document.RootElement.GetProperty("_links");
        var hrefs = links.EnumerateObject().SelectMany(link => link.Value.ValueKind == JsonValueKind.Array
            ? link.Value.EnumerateArray().Select(value => value.GetProperty("href").GetString() ?? string.Empty)
            : new[] { link.Value.GetProperty("href").GetString() ?? string.Empty }).ToArray();
        await Assert.That(hrefs.Length).IsGreaterThan(0);
        await Assert.That(hrefs.Any(href => href.Contains(AtprotoTransientApiFixture.Prefix, StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private byte[] CreateBody(string purpose, string digest, Guid tenant, string payload, int lifetimeSeconds = 60) =>
        JsonSerializer.SerializeToUtf8Bytes(new { purpose, tokenDigest = digest, tenantId = tenant,
            protectedPayload = payload, expiresAtUnixMilliseconds = fixture.Clock.GetUtcNow().AddSeconds(lifetimeSeconds).ToUnixTimeMilliseconds() });

    private static byte[] ConsumeBody(Guid id, string purpose, string digest, Guid tenant) =>
        JsonSerializer.SerializeToUtf8Bytes(new { candidateId = id, purpose, tokenDigest = digest, expectedTenantId = tenant });

    private async Task<HttpResponseMessage> SendAsync(string operation, byte[] body, CancellationToken cancellationToken = default)
    {
        using var request = fixture.Request(body, fixture.Sign(body, operation), operation);
        request.Headers.Add("Idempotency-Key", "transient-test-same-key");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        var response = await fixture.Client.SendAsync(request, cancellationToken);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        return response;
    }
}
