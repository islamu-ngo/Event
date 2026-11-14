using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Event.Web.BffHosting.Authentication;
using Explore.Blazor.IntegrationTests.Services;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[ClassDataSource<RedisBffTicketStoreTests.Authority>(Shared = SharedType.PerClass)]
public sealed class BffSessionAuthorityMiddlewareTests(RedisBffTicketStoreTests.Authority authority)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCookieOutageReturnsSafe503AndSameCookieRecoversBeforeLogoutRevokesIt(bool backendOutage)
    {
        using var store = authority.Create();
        var liveness = new ControlledLivenessFailure(store);
        await using var baseline = new BlazorBffWebApplicationFactory();
        await using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IExternalDataProtectionSource>(authority.Source));
            services.AddSingleton<IBffSessionAuthority>(liveness);
            services.AddScoped<BffAdminClaimsTransformation>();
            services.Replace(ServiceDescriptor.Scoped<EventBffTokenRefreshCookieEvents>(provider =>
            {
                var events = ActivatorUtilities.CreateInstance<EventBffTokenRefreshCookieEvents>(provider);
                var signingIn = events.OnSigningIn;
                events.OnSigningIn = async context =>
                {
                    await signingIn(context);
                    context.Principal = new ClaimsPrincipal(
                        new[] { new ClaimsIdentity() }.Concat(context.Principal!.Identities));
                };
                return events;
            }));
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.ForwardAuthenticate = null;
                options.ForwardChallenge = null;
                options.SessionStore = store;
                options.SlidingExpiration = false;
            });
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        await Assert.That(factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value.RequireAuthenticatedSignIn).IsTrue();
        await Assert.That(factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).EventsType == typeof(EventBffTokenRefreshCookieEvents)).IsTrue();
        using var login = await client.PostAsync("/test/session-authority/login", null);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var savedCookie = BffNativeSessionStoreLifecycleTests.Cookie(login);
        client.DefaultRequestHeaders.Add("Cookie", savedCookie);
        using var admitted = await client.GetAsync("/test/session-authority/protected");
        await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var admittedBody = JsonDocument.Parse(await admitted.Content.ReadAsStringAsync());
        await Assert.That(admittedBody.RootElement.GetProperty("anonymousFirst").GetBoolean()).IsTrue();
        await Assert.That(admittedBody.RootElement.GetProperty("authenticatedIdentities").GetInt32()).IsEqualTo(1);
        await Assert.That(admittedBody.RootElement.GetProperty("hasCanonicalBinding").GetBoolean()).IsTrue();
        using var status = await client.GetAsync("/auth/status");
        using var statusBody = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(statusBody.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();

        var options = authority.Options();
        var control = authority.Connection.GetServer(authority.Connection.GetEndPoints()[0]);
        if (backendOutage)
        {
            await control.ExecuteAsync("CLIENT", "PAUSE", 5_000, "ALL");
        }
        else liveness.Unavailable = true;
        try
        {
            using var outage = await client.GetAsync("/test/session-authority/protected");
            await Assert.That(outage.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(outage.Headers.Contains("Set-Cookie")).IsFalse();
            await Assert.That(outage.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            var body = await outage.Content.ReadAsStringAsync();
            await Assert.That(new[] { savedCookie, options.Endpoint!, options.Password! }
                .Any(value => body.Contains(value, StringComparison.Ordinal))).IsFalse();
            await Assert.That(body.Contains("Redis", StringComparison.OrdinalIgnoreCase)).IsFalse();

            using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            using var publicResponse = await anonymous.GetAsync("/test/session-authority/public");
            await Assert.That(publicResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            if (backendOutage)
            {
                // The pausing connection is paused too; observe the bounded
                // server-side pause ending instead of sending control behind it.
                await control.PingAsync();
            }
            else liveness.Unavailable = false;
        }

        if (backendOutage)
        {
            await Assert.That(authority.Options().Endpoint).IsEqualTo(options.Endpoint);
        }

        await Assert.That(await RecoveredSessionStatusAsync(client)).IsEqualTo(HttpStatusCode.OK);
        using var logout = await client.GetAsync("/auth/signout");
        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        using var replay = await client.GetAsync("/test/session-authority/protected");
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var missing = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var withoutCookie = await missing.GetAsync("/test/session-authority/protected");
        await Assert.That(withoutCookie.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpStatusCode> RecoveredSessionStatusAsync(HttpClient client)
    {
        var status = HttpStatusCode.ServiceUnavailable;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var response = await client.GetAsync("/test/session-authority/protected");
            status = response.StatusCode;
            if (status != HttpStatusCode.ServiceUnavailable) return status;
            await Task.Delay(100);
        }
        return status;
    }

    // The false variant supplies an outage after native retrieval; the true variant pauses Redis without replacing its address.
    private sealed class ControlledLivenessFailure(IBffSessionAuthority inner) : IBffSessionAuthority
    {
        public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(BffSessionBinding binding, CancellationToken cancellationToken) =>
            Unavailable ? throw new BffSessionAuthorityUnavailableException() : inner.IsActiveAsync(binding, cancellationToken);
    }
}

// This fixture establishes a native cookie, not Local-account or API-JWT authentication evidence.
[ApiController]
[Route("test/session-authority")]
public sealed class BffSessionAuthorityProbeController : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString("D"))], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        return Ok();
    }

    [Authorize]
    [HttpGet("protected")]
    public IActionResult Protected() => Ok(new
    {
        anonymousFirst = User.Identities.FirstOrDefault()?.IsAuthenticated == false,
        authenticatedIdentities = User.Identities.Count(identity => identity.IsAuthenticated),
        hasCanonicalBinding = BffSessionIdentity.TryGetBinding(User, out _)
    });

    [AllowAnonymous]
    [HttpGet("public")]
    public IActionResult Public() => Ok();
}
