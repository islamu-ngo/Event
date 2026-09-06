// ABOUTME: Migrates state-dependent ATProto endpoint security cases onto real API/PostgreSQL and Production BFF hosts.
// ABOUTME: Verifies callback rejection ordering, protected target propagation, cookie contracts and configured onboarding without bridge mocks.

extern alias bff;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.Identity;
using Explore.Atproto.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using BffAuth = bff::Explore.Blazor.Services.Auth;
using BffServices = bff::Explore.Blazor.Services;
using static Event.API.IntegrationTests.Authentication.AtprotoRelationalLoginFixture;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoRelationalLoginFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoRelationalEndpointSecurityTests(AtprotoRelationalLoginFixture fixture)
{
    [Test]
    public async Task MissingOrUnknownCallbackFlowFailsWithoutReflectingProviderMaterial()
    {
        await using var host = fixture.CreateBff();
        using var browser = BrowserClient(host, CanonicalOrigin, new CookieContainer());
        string state = RandomValue();
        string code = RandomValue();
        using var missing = await browser.GetAsync("/signin-atproto");
        using var unknown = await browser.GetAsync(Callback((state, code), "https://issuer-private.example/"));
        await AssertSafeCallbackFailure(missing);
        await AssertSafeCallbackFailure(unknown, state, code, "issuer-private.example");
    }

    [Test]
    public async Task IssuerSubstitutionLeavesStateAvailableForAValidBrowserRetry()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(fixture, browser, cookies);
        int verifications = fixture.External.VerifiedPdsRequests;
        using var rejected = await browser.GetAsync(Callback(flow, "https://attacker.example/"));
        await AssertSafeCallbackFailure(rejected, flow.State, flow.Code, "attacker.example");
        await Assert.That(fixture.External.VerifiedPdsRequests).IsEqualTo(verifications);
        await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
            .ReadAsync("oauth_state", flow.State)).IsNotNull();
        using var accepted = await browser.GetAsync(Callback(flow));
        await Assert.That(accepted.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(cookies.GetCookies(new Uri(CanonicalOrigin))[".AspNetCore.Cookies"]).IsNotNull();
        using var replay = await browser.GetAsync(Callback(flow));
        await AssertSafeCallbackFailure(replay, flow.State, flow.Code);
    }

    [Test]
    public async Task ProviderErrorConsumesOnlyTheBoundFlowAndNeverReflectsProviderContent()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(fixture, browser, cookies);
        string description = RandomValue();
        string errorCallback = "/signin-atproto?state=" + Uri.EscapeDataString(flow.State)
            + "&error=access_denied&error_description=" + description + "&iss=https%3A%2F%2Fissuer.example";
        using var first = await browser.GetAsync(errorCallback);
        await AssertSafeCallbackFailure(first, flow.State, description, "access_denied", "issuer.example");
        await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
            .ReadAsync("oauth_state", flow.State)).IsNull();
        using var replay = await browser.GetAsync(errorCallback);
        await AssertSafeCallbackFailure(replay, flow.State, description, "access_denied");
        await Assert.That(cookies.GetCookies(new Uri(CanonicalOrigin))[".AspNetCore.Cookies"]).IsNull();
    }

    [Test]
    public async Task MissingDuplicateOrAmbiguousResultsLeaveTheOriginalFlowConsumable()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(fixture, browser, cookies);
        string prefix = "/signin-atproto?state=" + Uri.EscapeDataString(flow.State) + "&iss=https%3A%2F%2Fissuer.example";
        foreach (string result in new[]
        {
            "", "&code=one&error=access_denied", "&code=one&error=bad!",
            "&error=access_denied&code=", "&code=one&code=two", "&error=access_denied&error=server_error"
        })
        {
            using var rejected = await browser.GetAsync(prefix + result);
            await AssertSafeCallbackFailure(rejected, flow.State, "access_denied", "server_error", "issuer.example");
        }
        await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
            .ReadAsync("oauth_state", flow.State)).IsNotNull();
        using var accepted = await browser.GetAsync(Callback(flow));
        await Assert.That(accepted.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(cookies.GetCookies(new Uri(CanonicalOrigin))[".AspNetCore.Cookies"]).IsNotNull();
    }

    [Test]
    public async Task UnsafePostedReturnPathFallsBackToRootWithoutTokenReflection()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(fixture, browser, cookies,
            new { handle = "alice.example", classification = "person", returnPath = "https://evil.example/steal" });
        using var callback = await browser.GetAsync(Callback(flow));
        await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(callback.Headers.Location?.OriginalString).IsEqualTo("/");
        string visible = string.Join('\n', callback.Headers.SelectMany(header => header.Value));
        var ticket = ReadTicket(host, cookies, CanonicalOrigin);
        foreach (string forbidden in new[] { "evil.example", fixture.External.AccessToken, fixture.External.RefreshToken, ticket.Properties.GetTokenValue("access_token")! })
            await Assert.That(visible.Contains(forbidden, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RepeatedChallengesReuseDiscoveryButPersistIndependentAuthorizationFlows()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        int dns = fixture.External.DnsRequests;
        int documents = fixture.External.DidDocumentRequests;
        int pars = fixture.External.PushedAuthorizationRequests;
        var first = await StartChallengeAsync(fixture, browser, cookies);
        var second = await StartChallengeAsync(fixture, browser, cookies);
        await Assert.That(first.State).IsNotEqualTo(second.State);
        await Assert.That(fixture.External.DnsRequests - dns).IsEqualTo(1);
        await Assert.That(fixture.External.DidDocumentRequests - documents).IsEqualTo(1);
        await Assert.That(fixture.External.PushedAuthorizationRequests - pars).IsEqualTo(2);
        var store = host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        await Assert.That(await store.ReadAsync("oauth_state", first.State)).IsNotNull();
        await Assert.That(await store.ReadAsync("oauth_state", second.State)).IsNotNull();
    }

    [Test]
    public async Task HostnameOnlyDidWebIdentityReachesItsPdsAndPersistsAuthorizationState()
    {
        fixture.External.UseDidWeb = true;
        try
        {
            await using var host = fixture.CreateBff();
            var cookies = new CookieContainer();
            using var browser = BrowserClient(host, CanonicalOrigin, cookies);
            int documents = fixture.External.DidDocumentRequests;
            int pars = fixture.External.PushedAuthorizationRequests;
            var flow = await StartChallengeAsync(fixture, browser, cookies);
            await Assert.That(fixture.External.DidDocumentRequests - documents).IsEqualTo(1);
            await Assert.That(fixture.External.PushedAuthorizationRequests - pars).IsEqualTo(1);
            await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
                .ReadAsync("oauth_state", flow.State)).IsNotNull();
        }
        finally { fixture.External.UseDidWeb = false; }
    }

    [Test]
    public async Task CanonicalActorTargetTravelsOnlyThroughProtectedStateAndTheSignedRealBridge()
    {
        using var observation = new BootstrapTargetObservation();
        await using var host = fixture.CreateBff().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(BffAuth.ApiBackedOAuthSessionStore.HttpClientName).AddHttpMessageHandler(() => observation)));
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        Guid actor = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        var flow = await StartChallengeAsync(fixture, browser, cookies, new
        {
            handle = "alice.example", classification = "organization", returnPath = "/events",
            canonicalActorId = actor, expectedCanonicalActorConcurrencyStamp = stamp
        });
        using var callback = await browser.GetAsync(Callback(flow));
        await Assert.That(observation.BodyActor).IsEqualTo(actor);
        await Assert.That(observation.SignedActor).IsEqualTo(actor);
        await Assert.That(observation.BodyStamp).IsEqualTo(stamp);
        await Assert.That(observation.SignedStamp).IsEqualTo(stamp);
        await Assert.That(observation.ResponseStatus).IsEqualTo(HttpStatusCode.Conflict);
        await AssertSafeCallbackFailure(callback, actor.ToString("D"), stamp.ToString("D"));
        await Assert.That(cookies.GetCookies(new Uri(CanonicalOrigin))[".AspNetCore.Cookies"]).IsNull();
    }

    [Test]
    public async Task WrongHostHandoffPreservesValidRetryAndProducesOnlyProtectedCookieClaims()
    {
        await using var host = fixture.CreateBff();
        var cookies = new CookieContainer();
        using var login = BrowserClient(host, TenantOrigin, cookies);
        using var canonical = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(fixture, login, cookies);
        using var callback = await canonical.GetAsync(Callback(flow));
        var destination = callback.Headers.Location!;
        await Assert.That(destination.GetLeftPart(UriPartial.Authority)).IsEqualTo(TenantOrigin);
        string code = QueryHelpers.ParseQuery(destination.Query)["code"].ToString();
        await Assert.That(code.Length).IsEqualTo(43);
        using var wrongHost = BrowserClient(host, "https://attacker.example.org", new CookieContainer());
        using var rejected = await wrongHost.GetAsync(destination.PathAndQuery);
        await Assert.That(rejected.Headers.Location?.OriginalString).IsEqualTo("/login?provider=atproto&challengeError=1");
        await AssertNoCookie(rejected);
        var otherCookies = new CookieContainer();
        using var otherBrowser = BrowserClient(host, TenantOrigin, otherCookies);
        _ = await StartChallengeAsync(fixture, otherBrowser, otherCookies);
        using var wrongProof = await otherBrowser.GetAsync(destination.PathAndQuery);
        await Assert.That(wrongProof.Headers.Location?.OriginalString).IsEqualTo("/login?provider=atproto&challengeError=1");
        await AssertNoCookie(wrongProof);
        using var accepted = await login.GetAsync(destination.PathAndQuery);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(accepted.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(accepted.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(await accepted.Content.ReadAsStringAsync()).IsEmpty();
        string header = accepted.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        foreach (string flag in new[] { "; path=/", "; secure", "; httponly", "; samesite=lax" })
            await Assert.That(header.ToLowerInvariant()).Contains(flag);
        var ticket = ReadTicket(host, cookies, TenantOrigin);
        string accessToken = ticket.Properties.GetTokenValue("access_token")!;
        string userId = new JsonWebToken(accessToken).Subject;
        await Assert.That(ticket.Principal.FindFirstValue("sub")).IsEqualTo(userId);
        await Assert.That(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo(userId);
        await Assert.That(ticket.Principal.FindFirstValue("did")).IsEqualTo(fixture.External.SubjectDid);
        await Assert.That(ticket.Principal.FindFirstValue("tenant_id")).IsEqualTo(fixture.TenantId.ToString("D"));
        await Assert.That(ticket.Principal.FindFirstValue("auth_provider")).IsEqualTo("atproto");
        await Assert.That(ticket.Principal.FindFirstValue("canonical_actor_id")).IsNull();
        await Assert.That(ticket.Principal.FindFirstValue("expected_actor_concurrency_stamp")).IsNull();
        await Assert.That(ticket.Properties.GetTokenValue("token_type")).IsEqualTo("Bearer");
        await Assert.That(DateTimeOffset.Parse(ticket.Properties.GetTokenValue("expires_at")!)).IsGreaterThan(DateTimeOffset.UtcNow);
        await Assert.That(ticket.Properties.AllowRefresh).IsTrue();
        await Assert.That(ticket.Properties.IsPersistent).IsTrue();
        string visible = header + destination;
        foreach (string secret in new[] { accessToken, fixture.External.AccessToken, fixture.External.RefreshToken, fixture.External.SubjectDid, userId })
            await Assert.That(visible.Contains(secret, StringComparison.Ordinal)).IsFalse();
        using var replay = await login.GetAsync(destination.PathAndQuery);
        await Assert.That(replay.Headers.Location?.OriginalString).IsEqualTo("/login?provider=atproto&challengeError=1");
        await AssertNoCookie(replay);
    }

    [Test]
    public async Task ConfiguredAtprotoPendingCompletesAndRefreshesRealAuthorityBeforeIssuingOneCookie()
    {
        await using var configured = new AtprotoRelationalLoginFixture { ConfiguredOnboarding = true };
        await configured.InitializeAsync();
        await using var host = configured.CreateBff();
        var statusProvider = host.Services.GetRequiredService<BffServices.IBffOnboardingStatusProvider>();
        await Assert.That((await statusProvider.GetStatusAsync()).Disposition)
            .IsEqualTo(BffServices.BffOnboardingDisposition.ConfiguredAdministratorPending);
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(configured, browser, cookies);
        using var callback = await browser.GetAsync(Callback(flow));
        await Assert.That(callback.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(callback.Headers.GetValues("Set-Cookie").Count(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That((await statusProvider.GetStatusAsync()).Disposition).IsEqualTo(BffServices.BffOnboardingDisposition.Completed);
        var ticket = ReadTicket(host, cookies, CanonicalOrigin);
        await Assert.That(ticket.Principal.HasClaim("explore:admin:instance", "true")).IsTrue();
        using var api = configured.Api.CreateClient();
        using var status = await api.GetAsync("/api/InstanceOnboarding/status");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("state").GetString()).IsEqualTo("Completed");
        string visible = string.Join('\n', callback.Headers.SelectMany(header => header.Value));
        foreach (string secret in new[] { ticket.Properties.GetTokenValue("access_token")!, configured.External.AccessToken, configured.External.RefreshToken })
            await Assert.That(visible.Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task LoginHintAuthorizationUrlFailsAfterRealStatePersistenceWithoutReflectingCredentials()
    {
        await using var host = fixture.CreateBff().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BffAuth.IAtprotoOAuthTransportFactory>();
            services.AddSingleton<BffAuth.IAtprotoOAuthTransportFactory>(new LoginHintMetadataTransport(fixture.External));
        }));
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, CanonicalOrigin, cookies);
        int pars = fixture.External.PushedAuthorizationRequests;
        int verifications = fixture.External.VerifiedPdsRequests;
        using var response = await SendChallengeAsync(browser, cookies);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Location).IsNull();
        string body = await response.Content.ReadAsStringAsync();
        foreach (string marker in new[] { "login_hint", "alice.example", fixture.External.AccessToken, fixture.External.RefreshToken })
            await Assert.That(body).DoesNotContain(marker);
        await Assert.That(fixture.External.PushedAuthorizationRequests - pars).IsEqualTo(1);
        await Assert.That(fixture.External.VerifiedPdsRequests).IsEqualTo(verifications);
        await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
            .ReadAsync("oauth_state", fixture.External.State!)).IsNotNull();
    }

    private static async Task<(string State, string Code)> StartChallengeAsync(AtprotoRelationalLoginFixture runtime,
        HttpClient browser, CookieContainer cookies, object? payload = null)
    {
        using var response = await SendChallengeAsync(browser, cookies, payload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string url = body.RootElement.GetProperty("authorizationUrl").GetString()!;
        await Assert.That(url).StartsWith("https://issuer.example/oauth/authorize?");
        await Assert.That(url).DoesNotContain("login_hint");
        return runtime.External.ResolveAuthorization(url);
    }

    [Test]
    public async Task HostRestartWithRetainedKeysCompletesAfterAKeyLossReplicaRejectsWithoutConsumption()
    {
        var cookies = new CookieContainer();
        (string State, string Code) flow;
        await using (var initial = fixture.CreateBff())
        {
            using var browser = BrowserClient(initial, CanonicalOrigin, cookies);
            flow = await StartChallengeAsync(fixture, browser, cookies);
        }
        await using (var lostKeys = fixture.CreateBff().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        })))
        {
            using var rejectedBrowser = BrowserClient(lostKeys, CanonicalOrigin, cookies);
            using var rejected = await rejectedBrowser.GetAsync(Callback(flow));
            await AssertSafeCallbackFailure(rejected, flow.State, flow.Code);
        }
        await using var restarted = fixture.CreateBff(rotateKeys: true);
        using var restoredBrowser = BrowserClient(restarted, CanonicalOrigin, cookies);
        var store = restarted.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        await Assert.That(await store.ReadAsync("oauth_state", flow.State)).IsNotNull();
        using var accepted = await restoredBrowser.GetAsync(Callback(flow));
        await Assert.That(accepted.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(cookies.GetCookies(new Uri(CanonicalOrigin))[".AspNetCore.Cookies"]).IsNotNull();
        await Assert.That(await store.ReadAsync("oauth_state", flow.State)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredRelationalStateOrHandoffNeverIssuesAnAuthenticationCookie(bool handoff)
    {
        var clock = new ExpiryClock();
        await using var timed = new AtprotoRelationalLoginFixture { Clock = clock };
        await timed.InitializeAsync();
        await using var host = timed.CreateBff();
        var cookies = new CookieContainer();
        using var browser = BrowserClient(host, handoff ? TenantOrigin : CanonicalOrigin, cookies);
        using var canonical = BrowserClient(host, CanonicalOrigin, cookies);
        var flow = await StartChallengeAsync(timed, browser, cookies);
        string path = Callback(flow);
        string locator = flow.State;
        if (handoff)
        {
            using var callback = await canonical.GetAsync(path);
            await Assert.That(callback.Headers.Location!.AbsolutePath).IsEqualTo("/auth/atproto/handoff");
            path = callback.Headers.Location.PathAndQuery;
            locator = QueryHelpers.ParseQuery(callback.Headers.Location.Query)["code"].ToString();
        }
        clock.Advance(TimeSpan.FromMinutes(handoff ? 3 : 11));
        using var rejected = await browser.GetAsync(path);
        if (handoff)
        {
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(rejected.Headers.Location?.OriginalString).IsEqualTo("/login?provider=atproto&challengeError=1");
            await AssertNoCookie(rejected);
        }
        else await AssertSafeCallbackFailure(rejected, flow.State, flow.Code);
        await Assert.That(cookies.GetCookies(browser.BaseAddress!)[".AspNetCore.Cookies"]).IsNull();
        await Assert.That(await host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>()
            .ReadAsync(handoff ? "tenant_handoff" : "oauth_state", locator, handoff ? timed.TenantId : null)).IsNull();
    }

    private sealed class ExpiryClock : TimeProvider
    {
        private long offsetTicks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref offsetTicks, elapsed.Ticks);
    }

    private static async Task<HttpResponseMessage> SendChallengeAsync(HttpClient browser, CookieContainer cookies, object? payload = null)
    {
        using var status = await browser.GetAsync("/auth/status");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string xsrf = Uri.UnescapeDataString(cookies.GetCookies(browser.BaseAddress!)["XSRF-TOKEN"]!.Value);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/atproto/challenge")
        {
            Content = JsonContent.Create(payload ?? new { handle = "alice.example", classification = "person", returnPath = "/events" })
        };
        request.Headers.Add("X-CSRF-TOKEN", xsrf);
        request.Headers.Add("Origin", browser.BaseAddress!.GetLeftPart(UriPartial.Authority));
        return await browser.SendAsync(request);
    }

    private static string Callback((string State, string Code) flow, string issuer = "https://issuer.example") =>
        "/signin-atproto?state=" + Uri.EscapeDataString(flow.State) + "&code=" + Uri.EscapeDataString(flow.Code) + "&iss=" + Uri.EscapeDataString(issuer);

    private static AuthenticationTicket ReadTicket(WebApplicationFactory<bff::Program> host, CookieContainer cookies, string origin) =>
        host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme)
            .TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookies.GetCookies(new Uri(origin))[".AspNetCore.Cookies"]!.Value))!;

    private static async Task AssertSafeCallbackFailure(HttpResponseMessage response, params string[] forbidden)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        string location = response.Headers.Location!.OriginalString;
        foreach (string required in new[] { "provider=atproto", "challengeError=1", "errorCode=atproto_callback_failed", "correlationId=" })
            await Assert.That(location).Contains(required);
        foreach (string value in forbidden) await Assert.That(location).DoesNotContain(value);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEmpty();
        await AssertNoCookie(response);
    }

    private static async Task AssertNoCookie(HttpResponseMessage response) =>
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)
            && values.Any(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal))).IsFalse();

    private static string RandomValue() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private sealed class LoginHintMetadataTransport(BffAuth.IAtprotoOAuthTransportFactory external) : BffAuth.IAtprotoOAuthTransportFactory
    {
        public IDnsResolver CreateDnsResolver() => external.CreateDnsResolver();
        public HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy policy, TimeSpan connectTimeout) =>
            new LoginHintMetadata { InnerHandler = external.CreatePrimaryHandler(policy, connectTimeout) };

        private sealed class LoginHintMetadata : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (request.RequestUri!.AbsolutePath == "/.well-known/oauth-authorization-server")
                {
                    var original = response.Content;
                    var document = JsonNode.Parse(await original.ReadAsStringAsync(cancellationToken))!;
                    document["authorization_endpoint"] = "https://issuer.example/oauth/authorize?login_hint=alice.example";
                    response.Content = JsonContent.Create(document);
                    original.Dispose();
                }
                return response;
            }
        }
    }

    private sealed class BootstrapTargetObservation : DelegatingHandler
    {
        public Guid? BodyActor { get; private set; }
        public Guid? BodyStamp { get; private set; }
        public Guid? SignedActor { get; private set; }
        public Guid? SignedStamp { get; private set; }
        public HttpStatusCode? ResponseStatus { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == BffAuth.AtprotoBootstrapAssertionService.BridgePath)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                BodyActor = body.RootElement.GetProperty("canonicalActorId").GetGuid();
                BodyStamp = body.RootElement.GetProperty("expectedCanonicalActorConcurrencyStamp").GetGuid();
                var assertion = new JsonWebToken(request.Headers.GetValues(BffAuth.AtprotoBootstrapAssertionService.HeaderName).Single());
                SignedActor = Guid.Parse(assertion.GetClaim(BffAuth.AtprotoBootstrapAssertionService.CanonicalActorIdClaim).Value);
                SignedStamp = Guid.Parse(assertion.GetClaim(BffAuth.AtprotoBootstrapAssertionService.ExpectedCanonicalActorConcurrencyStampClaim).Value);
            }
            var response = await base.SendAsync(request, cancellationToken);
            ResponseStatus = response.StatusCode;
            return response;
        }
    }
}
