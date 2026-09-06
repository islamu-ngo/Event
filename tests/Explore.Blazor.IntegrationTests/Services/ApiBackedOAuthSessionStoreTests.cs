// ABOUTME: Verifies the BFF sends CarpaNet session material only through the server-private authenticated bridge.
// ABOUTME: Proves bridge principal substitution fails closed before any cookie-ready flow result is captured.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class ApiBackedOAuthSessionStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");

    [Test]
    public async Task StoreUsesAuthenticatedTenantBridgeAndCapturesAuthenticatedPrincipalContract()
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var flow = BoundFlow();
        var handler = new BridgeHandler("did:plc:alice");
        var store = CreateStore(flow, handler);

        await store.StoreAsync("did:plc:alice", CreateSession(dpopKey));

        await Assert.That(flow.SessionResult).IsNotNull();
        await Assert.That(flow.SessionResult!.UserId).IsEqualTo(UserId);
        await Assert.That(flow.SessionResult.Did).IsEqualTo("did:plc:alice");
        await Assert.That(flow.SessionResult.Classification).IsEqualTo("person");
        await Assert.That(handler.TenantSlug).IsEqualTo("default");
        await Assert.That(handler.BootstrapAssertion).IsNotNull();
        await Assert.That(handler.RequestBody).Contains("oauth-access-token");
    }

    [Test]
    public async Task StorePreservesCanonicalActorTargetAndRejectsBridgeTargetSubstitution()
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var canonicalActorId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var flow = BoundFlow(canonicalActorId, expectedConcurrencyStamp);
        var handler = new BridgeHandler("did:plc:alice", canonicalActorId, expectedConcurrencyStamp);
        var store = CreateStore(flow, handler);

        await store.StoreAsync("did:plc:alice", CreateSession(dpopKey));

        await Assert.That(flow.SessionResult!.CanonicalActorId).IsEqualTo(canonicalActorId);
        await Assert.That(flow.SessionResult.ExpectedCanonicalActorConcurrencyStamp).IsEqualTo(expectedConcurrencyStamp);
        await Assert.That(handler.RequestBody).Contains(canonicalActorId.ToString("D"));
        await Assert.That(handler.BootstrapAssertion).Contains(".");
    }

    [Test]
    public async Task StoreRejectsBridgeDidSubstitutionWithoutCapturingCookiePrincipal()
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var flow = BoundFlow();
        var store = CreateStore(flow, new BridgeHandler("did:plc:attacker"));

        await Assert.That(async () => await store.StoreAsync("did:plc:alice", CreateSession(dpopKey)))
            .Throws<InvalidOperationException>();
        await Assert.That(flow.SessionResult).IsNull();
    }

    [Test]
    [Arguments("did:plc:attacker", "https://pds.example/")]
    [Arguments("did:plc:alice", "https://attacker-pds.example/")]
    public async Task StoreRejectsTokenSubjectOrPdsAudienceSubstitutionBeforeCallingApi(
        string tokenSubject,
        string tokenAudience)
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var flow = BoundFlow();
        var handler = new BridgeHandler("did:plc:alice");
        var store = CreateStore(flow, handler);
        var session = CreateSession(dpopKey);
        session.TokenSet.Sub = tokenSubject;
        session.TokenSet.Audience = tokenAudience;

        await Assert.That(async () => await store.StoreAsync("did:plc:alice", session))
            .Throws<InvalidOperationException>();
        await Assert.That(handler.CallCount).IsEqualTo(0);
        await Assert.That(flow.SessionResult).IsNull();
    }

    [Test]
    public async Task StoreRejectsSessionBeforeStateConsumptionWithoutCallingApi()
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var handler = new BridgeHandler("did:plc:alice");
        var store = CreateStore(new AtprotoOAuthFlowContext(), handler);

        await Assert.That(async () => await store.StoreAsync("did:plc:alice", CreateSession(dpopKey)))
            .Throws<InvalidOperationException>();
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task GetAndDeleteUseBearerAndPrivateAssertionForBoundTenantAndDid()
    {
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        var flow = BoundFlow();
        flow.CaptureSession(new(
            UserId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "did:plc:alice",
            "person",
            "opaque-platform-token",
            DateTimeOffset.UtcNow.AddMinutes(10)));
        var expected = CreateSession(dpopKey);
        var handler = new BridgeHandler("did:plc:alice", storedSession: expected);
        var store = CreateStore(flow, handler);

        var restored = await store.GetAsync("did:plc:alice");
        await store.DeleteAsync("did:plc:alice");

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.TokenSet.Sub).IsEqualTo("did:plc:alice");
        await Assert.That(restored.TokenSet.AccessToken).IsEqualTo("oauth-access-token");
        await Assert.That(handler.SessionRequests).Count().IsEqualTo(2);
        await Assert.That(handler.SessionRequests.All(request =>
            request.Path == "/api/auth/atproto/session/current"
            && request.TenantSlug == "default"
            && request.Authorization == "Bearer opaque-platform-token"
            && !string.IsNullOrWhiteSpace(request.PrivateAssertion))).IsTrue();
        await Assert.That(handler.SessionRequests.Select(request => request.Method))
            .IsEquivalentTo([HttpMethod.Get.Method, HttpMethod.Delete.Method]);
    }

    private static ApiBackedOAuthSessionStore CreateStore(
        AtprotoOAuthFlowContext flow,
        BridgeHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        return new(
            new FixedHttpClientFactory(client),
            new AtprotoBootstrapAssertionService(CreateKeyProvider(), TimeProvider.System),
            flow,
            TimeProvider.System,
            new AtprotoAuthenticationMetrics());
    }

    private static AtprotoOAuthFlowContext BoundFlow(
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null)
    {
        var flow = new AtprotoOAuthFlowContext();
        var challengeContext = new DefaultHttpContext();
        challengeContext.Request.Scheme = "https";
        challengeContext.Request.Host = new("events.example.com");
        var browserBinding = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), TimeProvider.System)
            .CreateBinding(challengeContext);
        flow.BindConsumedState(new(
            new(
                "did:plc:alice",
                new("https://pds.example/"),
                TenantId,
                "default",
                new("https://events.example.com/"),
                "/events",
                "oauth-active",
                "person",
                canonicalActorId,
                expectedCanonicalActorConcurrencyStamp) { BrowserBinding = browserBinding },
            new("https://issuer.example/")));
        return flow;
    }

    private static OAuthSessionData CreateSession(DPoPKeyPair key) => new()
    {
        DPoPKey = key.ExportKeyPair(),
        TokenSet = new TokenSet
        {
            Issuer = "https://issuer.example",
            Sub = "did:plc:alice",
            Audience = "https://pds.example/",
            Scope = "atproto transition:generic",
            AccessToken = "oauth-access-token",
            RefreshToken = "oauth-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        },
        ClientId = "https://events.example.com/oauth/client-metadata.json",
        RedirectUri = "https://events.example.com/signin-atproto",
        Scope = "atproto transition:generic"
    };

    private static AtprotoClientKeyProvider CreateKeyProvider()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var ring = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Encode(parameters.Q.X!),
                    y = Encode(parameters.Q.Y!),
                    d = Encode(parameters.D!),
                    kid = "oauth-active",
                    use = "sig",
                    alg = "ES256",
                    status = "active"
                }
            }
        });
        return new(Options.Create(new AtprotoClientKeyOptions { OAuthClientPrivateJwks = ring }));
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class BridgeHandler(
        string responseDid,
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null,
        OAuthSessionData? storedSession = null) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? TenantSlug { get; private set; }
        public string? BootstrapAssertion { get; private set; }
        public string? RequestBody { get; private set; }
        public List<SessionRequest> SessionRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            TenantSlug = request.Headers.GetValues("X-Tenant-Slug").Single();
            if (request.Method is { } method && method != HttpMethod.Post)
            {
                SessionRequests.Add(new(
                    method.Method,
                    request.RequestUri!.AbsolutePath,
                    TenantSlug,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.GetValues(AtprotoBootstrapAssertionService.SessionBridgeHeaderName).Single()));
                if (method == HttpMethod.Delete)
                {
                    return new(HttpStatusCode.NoContent);
                }

                var storedBody = JsonSerializer.Serialize(new
                {
                    did = responseDid,
                    expectedPdsUri = "https://pds.example/",
                    oauthClientKeyId = "oauth-active",
                    oauthSession = storedSession
                });
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(storedBody, Encoding.UTF8, "application/json")
                };
            }

            BootstrapAssertion = request.Headers.GetValues(AtprotoBootstrapAssertionService.HeaderName).Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Serialize(new
            {
                userId = UserId,
                actorId = Guid.NewGuid(),
                participationId = Guid.NewGuid(),
                did = responseDid,
                classification = "person",
                canonicalActorId,
                expectedCanonicalActorConcurrencyStamp,
                accessToken = "opaque-platform-token",
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        public sealed record SessionRequest(
            string Method,
            string Path,
            string TenantSlug,
            string? Authorization,
            string PrivateAssertion);
    }
}
