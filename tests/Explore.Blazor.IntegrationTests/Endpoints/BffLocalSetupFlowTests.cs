// ABOUTME: Runs native BFF setup cookies, antiforgery, and YARP enrichment against an HTTP contract upstream.
// ABOUTME: Proves browser headers cannot impersonate setup authority and Local completion creates no session.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffLocalSetupFlowTests
{
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments("POST", "/bff/setup-secret")]
    [Arguments("POST", "/bff/setup-secret/sync")]
    [Arguments("DELETE", "/bff/setup-secret")]
    public async Task SetupCookieMutationRequiresNativeAntiforgery(string method, string path)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { secret = fixture.Secret })
        };
        using HttpResponseMessage response = await fixture.Browser.SendAsync(request, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith("setup-secret=", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task NativeCookieEnrichmentProtectsStatusAndLocalCompletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        using (HttpResponseMessage setup = await fixture.Browser.GetAsync("/setup", CancellationToken))
            await Assert.That(setup.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (HttpResponseMessage withoutAuthority = await fixture.Browser.GetAsync("/onboarding/instance", CancellationToken))
            await Assert.That(withoutAuthority.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        string csrf = await fixture.CsrfAsync();
        using (var forged = new HttpRequestMessage(HttpMethod.Post, "/api/instanceonboarding/complete-local")
        {
            Content = JsonContent.Create(new { operationId = fixture.PendingOperationId })
        })
        {
            forged.Headers.Add("X-CSRF-TOKEN", csrf);
            forged.Headers.Add("X-Setup-Secret", fixture.Secret);
            using HttpResponseMessage rejected = await fixture.Browser.SendAsync(forged, CancellationToken);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fixture.Completed).IsFalse();
        }
        using (var persist = new HttpRequestMessage(HttpMethod.Post, "/bff/setup-secret")
        {
            Content = JsonContent.Create(new { secret = fixture.Secret })
        })
        {
            persist.Headers.Add("X-CSRF-TOKEN", csrf);
            using HttpResponseMessage persisted = await fixture.Browser.SendAsync(persist, CancellationToken);
            await Assert.That(persisted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            string cookie = persisted.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("setup-secret=", StringComparison.Ordinal));
            await Assert.That(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(cookie.Contains(fixture.Secret, StringComparison.Ordinal)).IsFalse();
        }
        using (HttpResponseMessage wizard = await fixture.Browser.GetAsync("/onboarding/instance", CancellationToken))
        {
            await Assert.That(wizard.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await wizard.Content.ReadAsStringAsync(CancellationToken)).Contains("autocomplete=\"username\"", StringComparison.Ordinal)).IsTrue();
        }
        using (HttpResponseMessage tenant = await fixture.Browser.GetAsync("/onboarding/tenant", CancellationToken))
            await Assert.That(tenant.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        using (var status = new HttpRequestMessage(HttpMethod.Get, "/api/instanceonboarding/status"))
        {
            status.Headers.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            using HttpResponseMessage response = await fixture.Browser.SendAsync(status, CancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken));
            await Assert.That(body.RootElement.GetProperty("pendingOperationId").GetGuid()).IsEqualTo(fixture.PendingOperationId);
        }
        var completion = new { operationId = fixture.PendingOperationId, username = "local-operator",
            temporaryPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}" };
        using HttpResponseMessage denied = await fixture.Browser.PostAsJsonAsync("/api/instanceonboarding/complete-local", completion, CancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(fixture.Completed).IsFalse();
        using var submit = new HttpRequestMessage(HttpMethod.Post, "/api/instanceonboarding/complete-local")
        {
            Content = JsonContent.Create(completion)
        };
        submit.Headers.Add("X-CSRF-TOKEN", csrf);
        submit.Headers.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using HttpResponseMessage completed = await fixture.Browser.SendAsync(submit, CancellationToken);
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Completed).IsTrue();
        await Assert.That(completed.Headers.TryGetValues("Set-Cookie", out var sessionCookies)
            && sessionCookies.Any(cookie => cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal))).IsFalse();
        using JsonDocument result = await JsonDocument.ParseAsync(await completed.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(result.RootElement.TryGetProperty("token", out _)).IsFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal string Secret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        internal Guid PendingOperationId { get; } = Guid.CreateVersion7();
        internal bool Completed { get; private set; }
        internal HttpClient Browser { get; private set; } = null!;
        private readonly BlazorBffWebApplicationFactory _root = new();
        private WebApplicationFactory<Program> _factory = null!;
        private WebApplication _upstream = null!;
        private HttpClient _api = null!;

        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try { await fixture.StartAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }
        private async Task StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _upstream = builder.Build();
            _upstream.MapGet("/api/system/onboarding-status", () => Results.Json(new { requiresOnboarding = !Completed, deploymentMode = "SingleTenant" }));
            _upstream.MapGet("/api/system/onboarding-preflight", () => Results.Json(new { isReadyToLaunch = true, blockingChecks = Array.Empty<object>(), warningChecks = Array.Empty<object>() }));
            _upstream.MapGet("/api/instance/settings/branding", () => Results.Json(new { defaultBrandDisplayName = "Local setup" }));
            _upstream.MapGet("/api/instance/settings/auth-provider/status", () => Results.Json(new { configured = true }));
            _upstream.MapGet("/api/instance/settings/authz-provider/status", () => Results.Json(new { configured = true }));
            _upstream.MapPost("/api/instanceonboarding/validate-secret", async (HttpContext context) =>
            {
                using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                return Results.Json(new { valid = body.RootElement.GetProperty("secret").GetString() == Secret });
            });
            _upstream.MapGet("/api/instanceonboarding/status", (HttpContext context) =>
                Results.Json(new { isCompleted = Completed, provider = "Local", state = Completed ? "Completed" : "InteractivePending",
                    mode = "Interactive", generation = 1, pendingOperationId = context.Request.Headers["X-Setup-Secret"] == Secret ? PendingOperationId : (Guid?)null }));
            _upstream.MapPost("/api/instanceonboarding/complete-local", async (HttpContext context) =>
            {
                if (context.Request.Headers["X-Setup-Secret"] != Secret) return Results.Unauthorized();
                using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                if (body.RootElement.GetProperty("operationId").GetGuid() != PendingOperationId) return Results.BadRequest();
                Completed = true;
                return Results.Json(new { success = true, id = PendingOperationId });
            });
            await _upstream.StartAsync(CancellationToken);
            string address = _upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            _api = new HttpClient { BaseAddress = new Uri(address) };
            _factory = _root.WithWebHostBuilder(host =>
            {
                host.UseSetting("ExploreApi:BaseUrl", address);
                host.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBffOnboardingStatusProvider>();
                    services.AddSingleton<IBffOnboardingStatusProvider, BffOnboardingStatusProvider>();
                    services.RemoveAll<IInstanceOnboardingClient>();
                    services.AddSingleton<IInstanceOnboardingClient>(new InstanceOnboardingClient(_api));
                });
            });
            Browser = _factory.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        }
        internal async Task<string> CsrfAsync()
        {
            using HttpResponseMessage response = await Browser.GetAsync("/auth/status", CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
            return Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        }
        public async ValueTask DisposeAsync()
        {
            Browser?.Dispose();
            if (_factory is not null) await _factory.DisposeAsync();
            await _root.DisposeAsync();
            _api?.Dispose();
            if (_upstream is not null) await _upstream.DisposeAsync();
        }
    }
}
