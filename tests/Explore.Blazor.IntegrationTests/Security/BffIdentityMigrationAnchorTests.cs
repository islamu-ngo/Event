using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Explore.Blazor.Client.Clients;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Security;

[Category("MigrationAnchor")]
public sealed class BffIdentityMigrationAnchorTests
{
    [Test]
    public async Task SetupSyncRejectsAmbiguousSmuggledAndWrongSchemePrincipals()
    {
        var rows = new Dictionary<string, ClaimsPrincipal>
        {
            ["duplicate-sub"] = Principal([new("sub", "one"), new("sub", "one")]),
            ["conflicting-sub"] = Principal([new("sub", "one"), new("sub", "two")]),
            ["conflicting-sid"] = Principal([new("sid", "one"), new("sid", "two")]),
            ["conflicting-nameidentifier"] = Principal([
                new(ClaimTypes.NameIdentifier, "one"), new(ClaimTypes.NameIdentifier, "two")]),
            ["conflicting-sub-nameidentifier"] = Principal([
                new("sub", "one"), new(ClaimTypes.NameIdentifier, "two")]),
            ["malformed-control"] = Principal([new("sub", "opaque\u0001value")]),
            ["unauthenticated-smuggling"] = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "smuggled")])),
            ["multiple-identities"] = new ClaimsPrincipal([
                new ClaimsIdentity([new Claim("sub", "one")], "OidcOne"),
                new ClaimsIdentity([new Claim("sub", "two")], "OidcTwo")]),
            ["wrong-purpose-scheme"] = Principal([new("sub", "api-key-subject")], "ApiKey")
        };
        var acceptedRows = new List<string>();

        foreach ((string name, ClaimsPrincipal principal) in rows)
        {
            await using var app = await CreateSetupAppAsync(principal);
            using var response = await app.Client.PostAsJsonAsync(
                "/bff/setup-secret/sync", new { secret = string.Empty });
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                acceptedRows.Add(name);
            }
        }

        await Assert.That(acceptedRows).IsEmpty();
    }

    [Test]
    public async Task RateLimiterUsesFailClosedNetworkPartitionForAmbiguousOrSmuggledIdentity()
    {
        await using var app = await CreateAmbiguousRateAppAsync();

        using var conflictOne = await app.Client.GetAsync("/conflict/one");
        using var conflictTwo = await app.Client.GetAsync("/conflict/two");
        using var smuggled = await app.Client.GetAsync("/smuggled/one");
        using var multiIdentity = await app.Client.GetAsync("/multi/one");
        using var crossType = await app.Client.GetAsync("/cross/one");

        await Assert.That(conflictOne.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(conflictTwo.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(smuggled.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(crossType.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(multiIdentity.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task AdminTransformationRejectsAmbiguousMultipleIdentityAndWrongSchemePrincipals()
    {
        var rows = new Dictionary<string, ClaimsPrincipal>
        {
            ["conflicting-sub"] = Principal([new("sub", "one"), new("sub", "two")]),
            ["conflicting-sid"] = Principal([new("sid", "one"), new("sid", "two")]),
            ["conflicting-sub-nameidentifier"] = Principal([
                new("sub", "one"), new(ClaimTypes.NameIdentifier, "two")]),
            ["multiple-identities"] = new ClaimsPrincipal([
                new ClaimsIdentity([new Claim("sub", "one")], "OidcOne"),
                new ClaimsIdentity([new Claim("sub", "two")], "OidcTwo")]),
            ["wrong-purpose-scheme"] = Principal([new("sub", "managed-subject")], "ManagedControlPlane")
        };
        var grantedRows = new List<string>();

        foreach ((string name, ClaimsPrincipal principal) in rows)
        {
            using var handler = new AdminAuthorityHandler();
            using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var readiness = Substitute.For<IBffOnboardingStatusProvider>();
            readiness.GetStatusAsync(Arg.Any<CancellationToken>())
                .Returns(new BffOnboardingStatus(
                    true, "Completed", "Interactive", null, 0, BffOnboardingDisposition.Completed));
            var service = new BffAdminClaimsTransformation(
                new FixedHttpClientFactory(client), cache, readiness,
                NullLogger<BffAdminClaimsTransformation>.Instance);

            if (await service.EnrichPrincipalAsync(principal, AccessTokenProperties()))
            {
                grantedRows.Add(name);
            }
        }

        await Assert.That(grantedRows).IsEmpty();
    }

    [Test]
    public async Task CircuitTokenResolutionRejectsConflictingSubjectClaims()
    {
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var token = Guid.CreateVersion7().ToString("N");
        var valid = Principal([new Claim("sub", "first-subject"), new Claim("sid", "session")]);
        valid.TryGetCircuitSubject(out var validSubject);
        valid.TryGetSessionId(out var validSession);
        store.Store(validSubject.PartitionKey, validSession.PartitionKey, token);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = Principal([
                new Claim("sub", "first-subject"),
                new Claim("sub", "second-subject"),
                new Claim("sid", "session")
            ])
        };
        var service = new CircuitAccessTokenService(
            store,
            new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context },
            NullLogger<CircuitAccessTokenService>.Instance);

        await Assert.That(service.AccessToken).IsNull();
        var crossType = Principal([
            new Claim("sub", "first-subject"),
            new Claim(ClaimTypes.NameIdentifier, "different-subject"),
            new Claim("sid", "legitimate-different-session")
        ]);
        await Assert.That(crossType.TryGetCircuitSubject(out _)).IsFalse();
        await Assert.That(crossType.TryGetSessionRefreshSubject(out _)).IsFalse();
        await Assert.That(crossType.TryGetSessionId(out var sessionOnly)).IsTrue();
        await Assert.That(sessionOnly.Value).IsEqualTo("legitimate-different-session");

    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims, string scheme = "Cookies") =>
        new(new ClaimsIdentity(claims, scheme));
    [Test]
    public async Task TrustedSchemesAndPurposesProduceDistinctOpaquePartitions()
    {
        const string equalSubject = "equal-opaque-value";
        const string differentSession = "different-session-value";
        string[] schemes = ["Cookies", "Keycloak", "Google", "Atproto"];
        var rateKeys = new HashSet<string>(StringComparer.Ordinal);
        var setupKeys = new HashSet<string>(StringComparer.Ordinal);
        var adminKeys = new HashSet<string>(StringComparer.Ordinal);
        var circuitKeys = new HashSet<string>(StringComparer.Ordinal);
        var refreshKeys = new HashSet<string>(StringComparer.Ordinal);
        var setupSessions = new SetupSecretSessionService();
        var circuitStore = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var allPurposeKeys = new HashSet<string>(StringComparer.Ordinal);
        using var handler = new AdminAuthorityHandler();
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var readiness = Substitute.For<IBffOnboardingStatusProvider>();
        readiness.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new BffOnboardingStatus(
                true, "Completed", "Interactive", null, 0, BffOnboardingDisposition.Completed));
        var admin = new BffAdminClaimsTransformation(
            new FixedHttpClientFactory(client), cache, readiness,
            NullLogger<BffAdminClaimsTransformation>.Instance);

        foreach (string scheme in schemes)
        {
            var principal = Principal([
                new Claim("sub", equalSubject),
                new Claim(ClaimTypes.NameIdentifier, equalSubject),
                new Claim("sid", differentSession)
            ], scheme);

            await Assert.That(principal.TryGetRatePartitionIdentity(out var rate)).IsTrue();
            await Assert.That(principal.TryGetSetupSessionIdentity(out var setup)).IsTrue();
            await Assert.That(principal.TryGetAdminSubject(out var adminIdentity)).IsTrue();
            await Assert.That(principal.TryGetCircuitSubject(out var circuit)).IsTrue();
            await Assert.That(principal.TryGetSessionRefreshSubject(out var refresh)).IsTrue();
            await Assert.That(principal.TryGetSessionId(out var session)).IsTrue();
            await Assert.That(rate.Source).IsEqualTo(EventBffOpaqueIdentitySource.ProviderSubject);
            await Assert.That(session.Source).IsEqualTo(EventBffOpaqueIdentitySource.SessionId);
            await Assert.That(rate.Value).IsEqualTo(equalSubject);
            await Assert.That(session.Value).IsEqualTo(differentSession);

            rateKeys.Add(rate.PartitionKey);
            setupKeys.Add(setup.PartitionKey);
            adminKeys.Add(adminIdentity.PartitionKey);
            circuitKeys.Add(circuit.PartitionKey);
            allPurposeKeys.Add(rate.PartitionKey);
            allPurposeKeys.Add(setup.PartitionKey);
            allPurposeKeys.Add(adminIdentity.PartitionKey);
            allPurposeKeys.Add(circuit.PartitionKey);
            allPurposeKeys.Add(refresh.PartitionKey);
            refreshKeys.Add(refresh.PartitionKey);
            setupSessions.SetForUser(setup.PartitionKey, scheme);
            circuitStore.Store(circuit.PartitionKey, session.PartitionKey, $"token-{scheme}");
            await Assert.That(await admin.EnrichPrincipalAsync(principal, AccessTokenProperties())).IsTrue();
            await Assert.That(setupSessions.GetForUser(setup.PartitionKey)).IsEqualTo(scheme);
            await Assert.That(circuitStore.ResolveByUserId(circuit.PartitionKey).Found).IsTrue();
        }

        await Assert.That(rateKeys.Count).IsEqualTo(4);
        await Assert.That(setupKeys.Count).IsEqualTo(4);
        await Assert.That(adminKeys.Count).IsEqualTo(4);
        await Assert.That(circuitKeys.Count).IsEqualTo(4);
        await Assert.That(refreshKeys.Count).IsEqualTo(4);
        await Assert.That(handler.RequestCount).IsEqualTo(4);
        await Assert.That(allPurposeKeys.Count).IsEqualTo(20);

        await using var rateApp = await CreateSchemeRateAppAsync(equalSubject);
        foreach (string scheme in schemes)
        {
            using var response = await rateApp.Client.GetAsync($"/scheme/{scheme}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using var repeatedCookie = await rateApp.Client.GetAsync("/scheme/Cookies");
        await Assert.That(repeatedCookie.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }


    private static AuthenticationProperties AccessTokenProperties()
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken
        {
            Name = "access_token",
            Value = Guid.CreateVersion7().ToString("N")
        }]);
        return properties;
    }

    private static async Task<TestApp> CreateSetupAppAsync(ClaimsPrincipal principal)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.UseTestServer(builder.WebHost);
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddRateLimiter(options => options.AddPolicy(
            RateLimitingExtensions.SetupSecretPolicy,
            _ => RateLimitPartition.GetNoLimiter<string>("anchor")));
        builder.Services.AddSingleton<SetupSecretSessionService>();
        builder.Services.AddSingleton<ISetupSecretSessionService>(provider =>
            provider.GetRequiredService<SetupSecretSessionService>());
        builder.Services.AddSingleton<ISetupSecretCookieProtector, PassThroughCookieProtector>();
        builder.Services.AddSingleton<ISetupSecretResolver>(
            new TrustedSetupSecretResolver(Guid.CreateVersion7().ToString("N")));
        builder.Services.AddSingleton(Substitute.For<IInstanceOnboardingClient>());

        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = principal;
            await next(context);
        });
        app.UseRateLimiter();
        app.MapSetupSecretEndpoints();
        await app.StartAsync();
        return new TestApp(app,
            Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestClient(app));
    }

    private static async Task<TestApp> CreateSchemeRateAppAsync(string opaqueValue)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.UseTestServer(builder.WebHost);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:DisableInTesting"] = "false",
            ["RateLimiting:SetupSecret:PermitLimit"] = "1",
            ["RateLimiting:SetupSecret:WindowSeconds"] = "60"
        });
        builder.Services.AddRouting();
        builder.Services.AddBffRateLimiting(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            string scheme = context.Request.Path.Value?.Split('/').LastOrDefault() ?? string.Empty;
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.20");
            context.User = Principal([new Claim("sub", opaqueValue)], scheme);
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/{group}/{scheme}", () => Microsoft.AspNetCore.Http.Results.Ok())
            .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy);
        await app.StartAsync();
        return new TestApp(app,
            Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestClient(app));
    }

    private static async Task<TestApp> CreateAmbiguousRateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.UseTestServer(builder.WebHost);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:DisableInTesting"] = "false",
            ["RateLimiting:SetupSecret:PermitLimit"] = "1",
            ["RateLimiting:SetupSecret:WindowSeconds"] = "60"
        });
        builder.Services.AddRouting();
        builder.Services.AddBffRateLimiting(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            context.User = context.Request.Path.Value switch
            {
                "/cross/one" => Principal([
                    new Claim("sub", "cross-one"), new Claim(ClaimTypes.NameIdentifier, "cross-two")]),
                "/conflict/one" => Principal([new("sub", "one"), new("sub", "two")]),
                "/conflict/two" => Principal([new("sub", "two"), new("sub", "one")]),
                "/smuggled/one" => new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "smuggled")])),
                "/multi/one" => new ClaimsPrincipal([
                    new ClaimsIdentity([new Claim("sub", "multi-one")], "One"),
                    new ClaimsIdentity([new Claim("sub", "multi-two")], "Two")]),
                _ => new ClaimsPrincipal()
            };
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/{group}/{name}", () => Microsoft.AspNetCore.Http.Results.Ok())
            .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy);
        await app.StartAsync();
        return new TestApp(app,
            Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestClient(app));
    }

    private sealed class TestApp(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AdminAuthorityHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new AdminAuthorityDto
            {
                IsInstanceAdmin = true,
                HasAnyAuthority = true,
                AdminTenantIds = [],
                AdminOrganizationIds = [],
                AdminGroupIds = []
            }), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TrustedSetupSecretResolver(string secret) : ISetupSecretResolver
    {
        public SetupSecretResolutionResult Resolve(
            Microsoft.AspNetCore.Http.HttpContext? httpContext = null,
            HttpRequestMessage? outboundRequest = null) =>
            SetupSecretResolutionResult.FoundFrom(SetupSecretSource.ServerSideSetupSession, secret);
    }

    private sealed class PassThroughCookieProtector : ISetupSecretCookieProtector
    {
        public string Protect(string secret) => secret;

        public bool TryUnprotect(string? protectedValue, out string? secret)
        {
            secret = protectedValue;
            return !string.IsNullOrWhiteSpace(secret);
        }
    }
}
