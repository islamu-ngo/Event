// ABOUTME: Exercises the actual Standalone composition and registered in-process transient bridge over PostgreSQL.
// ABOUTME: Verifies machine-only API identity and cookie isolation without replacing the internal HTTP dispatcher.

extern alias bff;
extern alias standalone;

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Infrastructure.Services.Federation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BffAuth = bff::Explore.Blazor.Services.Auth;
using StandaloneMarker = standalone::Event.Standalone.Hosting.StandaloneHostMarker;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoRelationalLoginFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class AtprotoCombinedTransientTests(AtprotoRelationalLoginFixture fixture)
{
    [Test]
    public async Task RegisteredCombinedTransport_ConsumesRelationalStateWithoutBrowserIdentityOrCookies()
    {
        var observations = new MachineIdentityObservation();
        await using var host = await CreateHostAsync(observations);
        _ = host.Server;
        await Assert.That(host.Services.GetRequiredService<IHostEnvironment>().IsProduction()).IsTrue();
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var browserContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D")),
                new Claim(ClaimTypes.Role, "admin")
            ], "browser"))
        };
        browserContext.Request.Headers.Cookie = "browser-session=" + Guid.CreateVersion7().ToString("N");
        accessor.HttpContext = browserContext;
        try
        {
            var store = host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
            string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            byte[] payload = RandomNumberGenerator.GetBytes(64);
            await Assert.That(await store.CreateAsync("oauth_state", token, fixture.TenantId, payload,
                DateTimeOffset.UtcNow.AddMinutes(1))).IsTrue();
            var candidate = await store.ReadAsync("oauth_state", token);
            await Assert.That(candidate).IsNotNull();
            await Assert.That(Convert.FromBase64String(candidate!.ProtectedPayload)).IsEquivalentTo(payload);
            await Assert.That(await store.ConsumeAsync(candidate)).IsTrue();
            await Assert.That(await store.ReadAsync("oauth_state", token)).IsNull();
            await Assert.That(observations.Requests.IsEmpty).IsFalse();
            foreach (var observation in observations.Requests)
            {
                await Assert.That(observation.Subject).IsEqualTo(AtprotoTransientAuthenticationDefaults.Subject);
                await Assert.That(observation.IsInProcess).IsTrue();
                await Assert.That(observation.HasCookie || observation.HasPlatformIdentity || observation.IsAdmin).IsFalse();
            }
            await Assert.That(accessor.HttpContext == browserContext).IsTrue();
            await Assert.That(browserContext.User.IsInRole("admin")).IsTrue();
        }
        finally { accessor.HttpContext = null; }
    }

    private async Task<WebApplicationFactory<StandaloneMarker>> CreateHostAsync(MachineIdentityObservation observations)
    {
        await using var scope = fixture.Api.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string connection = database.Database.GetConnectionString()!;
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretResolver>();
        string ring = (await secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks, null)).Value!;
        var configuration = new Dictionary<string, string?>
        {
            ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
            ["RateLimiting:DisableInTesting"] = "true",
            ["Testing:SkipJwtAuthorityWarmup"] = "true",
            ["SecretProvider:Provider"] = "Environment",
            ["Deployment:Mode"] = "SingleTenant",
            ["Deployment:DefaultTenantId"] = fixture.TenantId.ToString("D"),
            ["Authentication:Provider"] = "atproto",
            ["Authentication:AtprotoLoginEnabled"] = "true",
            ["Authorization:Provider"] = "local",
            ["PrivacyErasure:Authority:Topology"] = "CoLocated",
            ["Scheduler:Quartz:Enabled"] = "false",
            ["ConnectionStrings:cache"] = string.Empty,
            ["Atproto:PublicUrl"] = AtprotoRelationalLoginFixture.CanonicalOrigin,
            ["Atproto:CallbackPath"] = "/signin-atproto",
            ["Explore:MultiTenancy:DefaultTenantId"] = fixture.TenantId.ToString("D"),
            ["Explore:MultiTenancy:DefaultTenant"] = fixture.TenantSlug,
            ["Keycloak:Authority"] = "https://auth.example.test",
            ["Keycloak:Realm"] = "explore",
            ["Keycloak:Audience"] = "islamu-event-api",
            ["Mcp:Enabled"] = "false",
            ["HttpsRedirection:Enabled"] = "false"
        };
        TestDatabaseConfiguration.AddPostgreSql(configuration, connection);
        foreach (var setting in configuration.Where(setting => setting.Key.StartsWith("Database:Runtime:", StringComparison.Ordinal)).ToArray())
            configuration[setting.Key.Replace("Database:Runtime:", "Database:Migrator:", StringComparison.Ordinal)] = setting.Value;
        foreach (var setting in fixture.Api.Services.GetRequiredService<IConfiguration>()
            .GetSection("Instance:OperatorIdentity").AsEnumerable())
            configuration[setting.Key] = setting.Value;
        return new ProductionStandaloneFactory(configuration, builder =>
        {
            builder.UseEnvironment(Environments.Production);
            foreach (var setting in configuration) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureTestServices(services =>
            {
                TestHostServicePruner.RemoveNoisyHostedServices(services);
                services.RemoveExploreDbContextRegistrations();
                services.AddPostgreSqlExploreDbContext(connection);
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton(secrets);
                services.PostConfigure<BffAuth.AtprotoClientKeyOptions>(options => options.OAuthClientPrivateJwks = ring);
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(_ => fixture.CreateDataProtectionProvider());
                services.RemoveAll<BffAuth.IAtprotoOAuthTransportFactory>();
                services.AddSingleton<BffAuth.IAtprotoOAuthTransportFactory>(fixture.External);
                services.RemoveAll<AtprotoOAuthClientFactory>();
                services.AddScoped(provider => new AtprotoOAuthClientFactory(secrets,
                    provider.GetRequiredService<IOptions<AtprotoInfrastructureOptions>>(),
                    provider.GetRequiredService<IHostEnvironment>(), _ => fixture.External.CreateHandler()));
                services.RemoveAll<AtprotoCoreClientFactory>();
                services.AddScoped(provider => new AtprotoCoreClientFactory(
                    provider.GetRequiredService<AtprotoOAuthClientFactory>(), fixture.External));
                services.Configure<MvcOptions>(options => options.Filters.Add(observations));
                // Exercise the native handler's explicit Cookie-header exclusion as well as ambient-principal isolation.
                services.AddHttpClient(BffAuth.ApiBackedAtprotoTransientStore.HttpClientName)
                    .ConfigureHttpClient(client => client.DefaultRequestHeaders.Add("Cookie", "browser-session=" + Guid.CreateVersion7().ToString("N")));
            });
        });
    }

    private sealed class ProductionStandaloneFactory(IReadOnlyDictionary<string, string?> configuration,
        Action<IWebHostBuilder> configureWebHost)
        : WebApplicationFactory<StandaloneMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configureWebHost(builder);

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Database authority is selected before host callbacks. These disposable credentials must enter
            // through Environment, not ordinary configuration; the globally isolated class restores every key.
            var previous = new Dictionary<string, string?>();
            try
            {
                foreach (var setting in configuration)
                {
                    string key = setting.Key.Replace(":", "__", StringComparison.Ordinal);
                    previous[key] = Environment.GetEnvironmentVariable(key);
                    Environment.SetEnvironmentVariable(key, setting.Value);
                }
                return base.CreateHost(builder);
            }
            finally
            {
                foreach (var setting in previous)
                    Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }
        }
    }

    [Test]
    public async Task ProductionStandaloneCompletesOAuthLoginThroughItsOwnPrivateApiPipeline()
    {
        var observations = new MachineIdentityObservation();
        await using var host = await CreateHostAsync(observations);
        var cookies = new CookieContainer();
        using var browser = AtprotoRelationalLoginFixture.BrowserClient(host, AtprotoRelationalLoginFixture.CanonicalOrigin, cookies);
        await Assert.That(host.Services.GetRequiredService<IHostEnvironment>().IsProduction()).IsTrue();
        using var status = await browser.GetAsync("/auth/status");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/atproto/challenge")
        {
            Content = JsonContent.Create(new { handle = "alice.example", classification = "person", returnPath = "/events" })
        };
        request.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies.GetCookies(browser.BaseAddress!)["XSRF-TOKEN"]!.Value));
        request.Headers.Add("Origin", AtprotoRelationalLoginFixture.CanonicalOrigin);
        using var challenge = await browser.SendAsync(request);
        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await challenge.Content.ReadAsStringAsync());
        var authorization = fixture.External.ResolveAuthorization(body.RootElement.GetProperty("authorizationUrl").GetString()!);
        int verifications = fixture.External.VerifiedPdsRequests;
        using var callback = await browser.GetAsync("/signin-atproto?state=" + Uri.EscapeDataString(authorization.State)
            + "&code=" + Uri.EscapeDataString(authorization.Code) + "&iss=https%3A%2F%2Fissuer.example");
        await Assert.That(callback.Headers.Location?.OriginalString).IsEqualTo("/events");
        await Assert.That(fixture.External.VerifiedPdsRequests).IsGreaterThan(verifications);
        await Assert.That(cookies.GetCookies(browser.BaseAddress!)[".AspNetCore.Cookies"]).IsNotNull();
        await Assert.That(observations.Requests.IsEmpty).IsFalse();
        await Assert.That(observations.Requests.All(observation => observation.IsInProcess && !observation.HasCookie
            && !observation.HasPlatformIdentity && !observation.IsAdmin)).IsTrue();
        using var authenticated = await browser.GetAsync("/auth/status");
        using var authenticatedBody = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
        await Assert.That(authenticatedBody.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
    }

    private sealed class MachineIdentityObservation : IAsyncActionFilter
    {
        public ConcurrentQueue<(string? Subject, bool HasCookie, bool HasPlatformIdentity, bool IsAdmin, bool IsInProcess)> Requests { get; } = new();
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.HttpContext.Request.Path))
            {
                var user = context.HttpContext.User;
                Requests.Enqueue((user.FindFirstValue("sub"), context.HttpContext.Request.Headers.Cookie.Count != 0,
                    user.HasClaim(claim => claim.Type == ClaimTypes.NameIdentifier), user.IsInRole("admin"),
                    context.HttpContext.RequestServices.GetRequiredService<bff::Explore.Blazor.Services.InProcessEventApiDispatcher>()
                        .IsMarkedRequest(context.HttpContext)));
            }
            await next();
        }
    }
}
