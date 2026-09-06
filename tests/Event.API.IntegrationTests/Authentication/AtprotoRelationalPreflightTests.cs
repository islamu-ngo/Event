// ABOUTME: Verifies challenge rejection after native operational readiness against Production BFF and PostgreSQL.
// ABOUTME: Guards malformed input, hostile provider metadata and near-expiry proof without creating additional login state.

extern alias bff;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BffAuth = bff::Explore.Blazor.Services.Auth;
using static Event.API.IntegrationTests.Authentication.AtprotoRelationalLoginFixture;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoRelationalLoginFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoRelationalPreflightTests(AtprotoRelationalLoginFixture fixture)
{
    [Test]
    public async Task InvalidMissingAndOversizedHandlesAreRejectedWithoutCredentialReflection()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var client = BrowserClient(host, CanonicalOrigin, cookies);
        string[] payloads =
        [
            "{}", "{\"handle\":\"\",\"classification\":\"person\"}",
            "{\"handle\":\"single-label\",\"classification\":\"person\"}",
            "{\"handle\":\"bad..example\",\"classification\":\"person\"}",
            JsonSerializer.Serialize(new { handle = $"oauth-access-token.{new string('a', 240)}.example", classification = "person" })
        ];
        int beforePar = fixture.External.PushedAuthorizationRequests;
        foreach (string payload in payloads)
        {
            using var response = await ChallengeAsync(client, cookies, payload);
            await AssertRejectedAsync(response, "oauth-access-token", "login_hint", "credential", new string('x', 64));
            await AssertNoLoginStateAsync(fixture);
        }
        using var oversized = await ChallengeAsync(client, cookies, JsonSerializer.Serialize(new
        {
            handle = "alice.example", classification = "person", padding = new string('x', 2200)
        }));
        await Assert.That(oversized.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(oversized.Headers.Location).IsNull();
        await Assert.That(await oversized.Content.ReadAsStringAsync()).DoesNotContain(new string('x', 64));
        await AssertNoLoginStateAsync(fixture);
        await Assert.That(fixture.External.PushedAuthorizationRequests).IsEqualTo(beforePar);
    }

    [Test]
    public async Task MissingOrUnknownClassificationIsRejectedBeforeOAuthStateCreation()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var client = BrowserClient(host, CanonicalOrigin, cookies);
        int beforePar = fixture.External.PushedAuthorizationRequests;
        foreach (string payload in new[] { "{\"handle\":\"alice.example\"}", "{\"handle\":\"alice.example\",\"classification\":\"bot\"}" })
        {
            using var response = await ChallengeAsync(client, cookies, payload);
            await AssertRejectedAsync(response, "login_hint", "access_token");
            await AssertNoLoginStateAsync(fixture);
        }
        await Assert.That(fixture.External.PushedAuthorizationRequests).IsEqualTo(beforePar);
    }

    [Test]
    [Arguments("http")]
    [Arguments("userinfo")]
    [Arguments("fragment")]
    public async Task UnsafeAuthorizationEndpointFailsThroughHttpWithoutCredentialReflection(string scenario)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        fixture.External.AuthorizationEndpoint = scenario switch
        {
            "http" => "http://issuer.example/oauth/authorize",
            "userinfo" => $"https://user:{canary}@issuer.example/oauth/authorize",
            "fragment" => $"https://issuer.example/oauth/authorize#access_token={canary}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        try
        {
            await using var host = fixture.CreateBff();
            var cookies = new CookieContainer();
            using var client = BrowserClient(host, CanonicalOrigin, cookies);
            int beforePar = fixture.External.PushedAuthorizationRequests;
            int beforeMetadata = fixture.External.AuthorizationMetadataRequests;
            using var response = await ChallengeAsync(client, cookies, "{\"handle\":\"alice.example\",\"classification\":\"person\"}");
            await AssertRejectedAsync(response, "login_hint", "access_token", "user:", canary);
            await Assert.That(fixture.External.AuthorizationMetadataRequests).IsGreaterThan(beforeMetadata);
            await Assert.That(fixture.External.PushedAuthorizationRequests).IsEqualTo(beforePar);
            await AssertNoLoginStateAsync(fixture);
        }
        finally { fixture.External.AuthorizationEndpoint = "https://issuer.example/oauth/authorize"; }
    }

    [Test]
    [Arguments("conflicting_handle")]
    [Arguments("missing_pds")]
    [Arguments("duplicate_pds")]
    [Arguments("non_https_pds")]
    [Arguments("invalid_pds")]
    public async Task ConflictingHandleOrInvalidPdsServiceFailsBeforeParAndBridge(string scenario)
    {
        fixture.External.IdentityDocumentScenario = scenario;
        try
        {
            await using var host = fixture.CreateBff();
            var cookies = new CookieContainer();
            using var client = BrowserClient(host, CanonicalOrigin, cookies);
            int beforePar = fixture.External.PushedAuthorizationRequests;
            int beforeDocuments = fixture.External.DidDocumentRequests;
            using var response = await ChallengeAsync(client, cookies, "{\"handle\":\"alice.example\",\"classification\":\"person\"}");
            await AssertRejectedAsync(response, "login_hint", "access_token", "mallory.example", "not-a-uri");
            await Assert.That(fixture.External.DidDocumentRequests).IsGreaterThan(beforeDocuments);
            await Assert.That(fixture.External.PushedAuthorizationRequests).IsEqualTo(beforePar);
            await AssertNoLoginStateAsync(fixture);
        }
        finally { fixture.External.IdentityDocumentScenario = null; }
    }

    [Test]
    public async Task NearExpiryChallenge_ReturnsBoundedRetryAfterWithoutRewritingTheProofCookie()
    {
        var clock = new Clock();
        await using var timedFixture = new AtprotoRelationalLoginFixture { Clock = clock };
        await timedFixture.InitializeAsync();
        await using var host = timedFixture.CreateBff();
        var cookies = new CookieContainer();
        using var client = BrowserClient(host, CanonicalOrigin, cookies);
        const string payload = "{\"handle\":\"alice.example\",\"classification\":\"person\"}";
        using var initial = await ChallengeAsync(client, cookies, payload);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var origin = new Uri(CanonicalOrigin);
        string originalCookie = cookies.GetCookies(origin)[BffAuth.AtprotoBrowserProof.CookieName]!.Value;
        var proof = host.Services.GetRequiredService<BffAuth.AtprotoBrowserProof>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = origin.Scheme;
        context.Request.Host = new(origin.Authority);
        context.Request.Headers.Cookie = cookies.GetCookieHeader(origin);
        var binding = proof.CreateBinding(context);
        await Assert.That(context.Response.Headers.SetCookie.Count).IsEqualTo(0);
        await using var scope = timedFixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        int beforeRows = await db.Set<AtprotoTransientRecord>().CountAsync(row => row.Purpose == AtprotoTransientPurpose.OAuthState);
        await Assert.That(beforeRows).IsEqualTo(1);
        int beforePar = timedFixture.External.PushedAuthorizationRequests;

        clock.Advance(TimeSpan.FromMinutes(13.5));
        using var response = await ChallengeAsync(client, cookies, payload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(response.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(90));
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var replacements)
            && replacements.Any(value => value.StartsWith(BffAuth.AtprotoBrowserProof.CookieName + "=", StringComparison.Ordinal))).IsFalse();
        await Assert.That(cookies.GetCookies(origin)[BffAuth.AtprotoBrowserProof.CookieName]!.Value).IsEqualTo(originalCookie);
        await Assert.That(proof.Validate(context.Request, binding)).IsTrue();
        await Assert.That(timedFixture.External.PushedAuthorizationRequests).IsEqualTo(beforePar);
        await Assert.That(await db.Set<AtprotoTransientRecord>().CountAsync(row => row.Purpose == AtprotoTransientPurpose.OAuthState)).IsEqualTo(beforeRows);
    }

    private static async Task<HttpResponseMessage> ChallengeAsync(HttpClient client, CookieContainer cookies, string payload)
    {
        using var status = await client.GetAsync("/auth/status");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string xsrf = Uri.UnescapeDataString(cookies.GetCookies(new Uri(CanonicalOrigin))["XSRF-TOKEN"]!.Value);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/atproto/challenge")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", xsrf);
        request.Headers.Add("Origin", CanonicalOrigin);
        return await client.SendAsync(request);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, params string[] markers)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Location).IsNull();
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("ATProto sign-in could not be started.");
        foreach (string marker in markers) await Assert.That(body).DoesNotContain(marker);
    }

    private static async Task AssertNoLoginStateAsync(AtprotoRelationalLoginFixture fixture)
    {
        await using var scope = fixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.Set<AtprotoTransientRecord>().AnyAsync(row => row.Purpose != AtprotoTransientPurpose.HealthProbe)).IsFalse();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
