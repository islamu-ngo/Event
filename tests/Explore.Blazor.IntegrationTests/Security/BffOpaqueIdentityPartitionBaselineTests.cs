using System.Threading.RateLimiting;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Security;

public sealed class BffOpaqueIdentityPartitionBaselineTests
{
    private const string ProviderSubject = "provider-subject-opaque";
    private const string SessionOne = "session-opaque-one";
    private const string SessionTwo = "session-opaque-two";

    [Test]
    public async Task SetupRateLimiterPartitionsByProviderSubjectThenSessionAndFailsClosedToNetwork()
    {
        await using var app = await CreateRateLimitAppAsync();

        using var subjectFirst = await app.Client.GetAsync("/subject/one");
        using var subjectSecond = await app.Client.GetAsync("/subject/two");
        using var sessionFirst = await app.Client.GetAsync("/session/one");
        using var sessionSecond = await app.Client.GetAsync("/session/two");
        using var missingFirst = await app.Client.GetAsync("/missing/one");
        using var missingSecond = await app.Client.GetAsync("/missing/two");

        await Assert.That(subjectFirst.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(subjectSecond.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(sessionFirst.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(sessionSecond.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(missingFirst.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(missingSecond.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task SetupSyncBindsTrustedSessionToOpaqueProviderSubjectNotSidOrPlatformGuid()
    {
        var platformId = Guid.Parse("0190f50d-1690-7000-8000-000000000001");
        var principal = CreatePrincipal([
            new Claim("sub", ProviderSubject),
            new Claim("sid", SessionOne),
            new Claim("internal_user_id", platformId.ToString("D"))
        ]);
        await using var app = await CreateSetupAppAsync(principal);

        using var response = await app.Client.PostAsJsonAsync(
            "/bff/setup-secret/sync", new { secret = string.Empty });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.SetupSessions.GetForUser(SetupKey(principal))).IsEqualTo(app.Secret);
        await Assert.That(app.SetupSessions.GetForUser(SessionOne)).IsNull();
        await Assert.That(app.SetupSessions.GetForUser(platformId.ToString("D"))).IsNull();
    }

    [Test]
    public async Task SetupSyncWithOnlyPlatformIdentityClaimFailsClosedWithoutBindingSession()
    {
        var platformId = Guid.Parse("0190f50d-1690-7000-8000-000000000002");
        await using var app = await CreateSetupAppAsync(CreatePrincipal([
            new Claim("internal_user_id", platformId.ToString("D"))
        ]));

        using var response = await app.Client.PostAsJsonAsync(
            "/bff/setup-secret/sync", new { secret = string.Empty });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(app.SetupSessions.GetForUser(platformId.ToString("D"))).IsNull();
    }

    [Test]
    public async Task SetupSyncSupportsSidOnlyAndNameIdentifierOnlyPurposeFallbacks()
    {
        var sidPrincipal = CreatePrincipal([new Claim("sid", SessionOne)]);
        await using var sidApp = await CreateSetupAppAsync(sidPrincipal);
        using var sidResponse = await sidApp.Client.PostAsJsonAsync(
            "/bff/setup-secret/sync", new { secret = string.Empty });

        var namePrincipal = CreatePrincipal([
            new Claim(ClaimTypes.NameIdentifier, ProviderSubject)
        ]);
        await using var nameApp = await CreateSetupAppAsync(namePrincipal);
        using var nameResponse = await nameApp.Client.PostAsJsonAsync(
            "/bff/setup-secret/sync", new { secret = string.Empty });

        await Assert.That(sidResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(sidApp.SetupSessions.GetForUser(SetupKey(sidPrincipal))).IsEqualTo(sidApp.Secret);
        await Assert.That(nameResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(nameApp.SetupSessions.GetForUser(SetupKey(namePrincipal))).IsEqualTo(nameApp.Secret);
    }

    [Test]
    public async Task SetupSyncWhitespaceSubjectFailsClosedWithoutFallingThroughToSid()
    {
        await using var app = await CreateSetupAppAsync(CreatePrincipal([
            new Claim("sub", "   "),
            new Claim("sid", SessionOne)
        ]));

        using var response = await app.Client.PostAsJsonAsync(
            "/bff/setup-secret/sync", new { secret = string.Empty });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(app.SetupSessions.GetForUser(SessionOne)).IsNull();
    }
    [Test]
    public async Task CircuitTokenStoreSeparatesEqualOpaqueValuesUsedForDifferentPurposes()
    {
        const string equalOpaqueValue = "equal-opaque-value";
        var subjectPurposeToken = Guid.CreateVersion7().ToString("N");
        var sessionPurposeToken = Guid.CreateVersion7().ToString("N");
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);

        var subject = CreatePrincipal([new Claim("sub", equalOpaqueValue)]);
        var subjectSession = CreatePrincipal([new Claim("sid", "other-session")]);
        var otherSubject = CreatePrincipal([new Claim("sub", "other-provider-subject")]);
        var session = CreatePrincipal([new Claim("sid", equalOpaqueValue)]);
        store.Store(CircuitKey(subject), SessionKey(subjectSession), subjectPurposeToken);
        store.Store(CircuitKey(otherSubject), SessionKey(session), sessionPurposeToken);

        var subjectPurpose = store.Resolve(CircuitKey(subject), SessionKey(subjectSession));
        var sessionPurpose = store.Resolve(CircuitKey(otherSubject), SessionKey(session));
        var collapsed = store.Resolve(CircuitKey(subject), SessionKey(session));

        await Assert.That(subjectPurpose.Token).IsEqualTo(subjectPurposeToken);
        await Assert.That(sessionPurpose.Token).IsEqualTo(sessionPurposeToken);
        await Assert.That(collapsed.Found).IsFalse();
    }

    [Test]
    public async Task AnonymousRatePartitionsUseStableRemoteAddressWithoutCrossAddressCollapse()
    {
        await using var app = await CreateRateLimitAppAsync();

        using var firstAddressFirst = await app.Client.GetAsync("/network/one-a");
        using var firstAddressSecond = await app.Client.GetAsync("/network/one-b");
        using var secondAddressFirst = await app.Client.GetAsync("/network/two-a");

        await Assert.That(firstAddressFirst.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(firstAddressSecond.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(secondAddressFirst.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static ClaimsPrincipal CreatePrincipal(IEnumerable<Claim> claims, string scheme = "Cookies") =>
        new(new ClaimsIdentity(claims, scheme));

    private static string SetupKey(ClaimsPrincipal principal) =>
        principal.TryGetSetupSessionIdentity(out var identity) ? identity.PartitionKey : string.Empty;

    private static string CircuitKey(ClaimsPrincipal principal) =>
        principal.TryGetCircuitSubject(out var identity) ? identity.PartitionKey : string.Empty;

    private static string? SessionKey(ClaimsPrincipal principal) =>
        principal.TryGetSessionId(out var identity) ? identity.PartitionKey : null;

    private static async Task<TestBffApp> CreateRateLimitAppAsync()
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
            context.Connection.RemoteIpAddress = context.Request.Path.Value switch
            {
                string path when path.StartsWith("/network/two", StringComparison.Ordinal) => IPAddress.Parse("198.51.100.22"),
                string path when path.StartsWith("/network/", StringComparison.Ordinal) => IPAddress.Parse("198.51.100.11"),
                _ => context.Connection.RemoteIpAddress
            };
            Claim[] claims = context.Request.Path.Value switch
            {
                "/subject/one" => [new("sub", ProviderSubject), new("sid", SessionOne)],
                "/subject/two" => [new("sub", ProviderSubject), new("sid", SessionTwo)],
                "/session/one" => [new("sid", SessionOne)],
                "/session/two" => [new("sid", SessionTwo)],
                _ => []
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/{group}/{name}", () => Microsoft.AspNetCore.Http.Results.Ok())
            .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy);
        await app.StartAsync();
        return new TestBffApp(app,
            Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestClient(app));
    }

    private static async Task<TestBffApp> CreateSetupAppAsync(ClaimsPrincipal principal)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.UseTestServer(builder.WebHost);
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddRateLimiter(options => options.AddPolicy(
            RateLimitingExtensions.SetupSecretPolicy,
            _ => RateLimitPartition.GetNoLimiter<string>("baseline")));

        var secret = Guid.CreateVersion7().ToString("N");
        var sessions = new SetupSecretSessionService();
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton<ISetupSecretSessionService>(sessions);
        builder.Services.AddSingleton<ISetupSecretCookieProtector, PassThroughCookieProtector>();
        builder.Services.AddSingleton<ISetupSecretResolver>(new TrustedSetupSecretResolver(secret));
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
        return new TestBffApp(app,
            Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestClient(app), sessions, secret);
    }

    private sealed class TestBffApp(
        WebApplication app,
        HttpClient client,
        SetupSecretSessionService? sessions = null,
        string? secret = null) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public SetupSecretSessionService SetupSessions { get; } = sessions ?? new();
        public string Secret { get; } = secret ?? string.Empty;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
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
