
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Explore.Blazor.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed partial class GuestRegistrationStatusBffTests
{
    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Test]
    [Arguments(200)]
    [Arguments(404)]
    [Arguments(503)]
    public async Task ProxyPreservesHeaderOnlyAuthorityAndOverridesPublicDownstreamHeaders(int status)
    {
        await using var upstream = await Upstream.StartAsync(status);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("ExploreApi:BaseUrl", upstream.Address));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        string token = Secret();
        string path = $"/api/events/{Guid.CreateVersion7()}/guest-registration-orders/{Guid.CreateVersion7()}/status";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Registration-Order-Capability", token);
        using var response = await client.SendAsync(request, Cancellation);
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await AssertPrivateAsync(response);
        await Assert.That(upstream.Capability == token).IsTrue();
        await Assert.That(upstream.PathAndQuery).IsEqualTo(path);
        await Assert.That(upstream.Referrer).IsNull();
        await Assert.That(await response.Content.ReadAsStringAsync(Cancellation)).DoesNotContain(token);
    }

    [Test]
    [Arguments(null, 204)]
    [Arguments("invalid", 204)]
    [Arguments("valid", 204)]
    [Arguments("valid", 404)]
    [Arguments("valid", 409)]
    [Arguments("valid", 503)]
    public async Task CancellationUsesNativeCsrfBoundaryAndPrivateSanitizedProxy(string? csrf, int status)
    {
        await using var upstream = await Upstream.StartAsync(status);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("ExploreApi:BaseUrl", upstream.Address));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var landing = await client.GetAsync("/auth/status", Cancellation);
        var cookies = landing.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]).ToArray();
        var tokenCookie = cookies.Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        string capability = Secret();
        string path = $"/api/events/{Guid.CreateVersion7()}/guest-registration-orders/{Guid.CreateVersion7()}/cancellation";
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf == "valid" ? Uri.UnescapeDataString(tokenCookie["XSRF-TOKEN=".Length..]) : Secret());
        request.Headers.Add("X-Registration-Order-Capability", capability);
        request.Headers.Add("X-Setup-Secret", Secret());
        request.Headers.Add("X-API-Key", Secret());
        request.Headers.Add("X-Tenant-Id", Guid.CreateVersion7().ToString());
        request.Headers.Add("X-Tenant-Slug", "spoofed-tenant");
        request.Headers.Add("X-Support-Access-Session-Id", Guid.CreateVersion7().ToString());
        request.Headers.Authorization = new("Bearer", Secret());
        using var response = await client.SendAsync(request, Cancellation);
        await Assert.That((int)response.StatusCode).IsEqualTo(csrf == "valid" ? status : 400);
        await AssertPrivateAsync(response);
        await Assert.That(upstream.RequestCount).IsEqualTo(csrf == "valid" ? 1 : 0);
        if (csrf != "valid") return;
        await Assert.That(upstream.Capability == capability).IsTrue();
        await Assert.That(upstream.PathAndQuery).IsEqualTo(path);
        await Assert.That(upstream.Method).IsEqualTo("POST");
        await Assert.That(upstream.Body).IsEqualTo(string.Empty);
        await Assert.That(upstream.PrivilegedHeaders.Count).IsEqualTo(0);
        await Assert.That(await response.Content.ReadAsStringAsync(Cancellation)).DoesNotContain(capability);
    }

    [Test]
    public async Task CancellationQueryInputIsScrubbedWithoutCreatingAuthority()
    {
        await using var upstream = await Upstream.StartAsync(404);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("ExploreApi:BaseUrl", upstream.Address));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var landing = await client.GetAsync("/auth/status", Cancellation);
        var cookies = landing.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]).ToArray();
        string path = $"/api/events/{Guid.CreateVersion7()}/guest-registration-orders/{Guid.CreateVersion7()}/cancellation";
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "?capability=" + Secret());
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        request.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies.Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))["XSRF-TOKEN=".Length..]));
        using var response = await client.SendAsync(request, Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(upstream.Capability).IsNull();
        await Assert.That(upstream.PathAndQuery).IsEqualTo(path);
        await AssertPrivateAsync(response);
    }

    [Test]
    public async Task QueryOnlyInputNeverBecomesAForwardedCapabilityHeader()
    {
        await using var upstream = await Upstream.StartAsync(404);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.UseSetting("ExploreApi:BaseUrl", upstream.Address));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/api/events/{Guid.CreateVersion7()}/guest-registration-orders/{Guid.CreateVersion7()}/status?capability={Secret()}", Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(upstream.Capability).IsNull();
        await Assert.That(upstream.PathAndQuery).DoesNotContain("?");
        await AssertPrivateAsync(response);
    }

    [Test]
    [Arguments("")]
    [Arguments("/nested/community")]
    public async Task LandingFirstHeadChildIsExactShippedInlineScrubberBeforeAnyNetworkOrAnalytics(string pathBase)
    {
        await using var factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("PublicBaseUrl", "https://localhost" + pathBase + "/"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        string token = Secret();
        using var response = await client.GetAsync($"{pathBase}/registration/guest/events/{Guid.CreateVersion7()}/orders/{Guid.CreateVersion7()}/status?capability={token}", Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(response);
        string html = await response.Content.ReadAsStringAsync(Cancellation);
        if (Environment.GetEnvironmentVariable("GUEST_STATUS_EVIDENCE") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, pathBase.Length == 0 ? "landing.html" : "landing-pathbase.html"), html, Cancellation);
        }
        var first = FirstHeadScript().Match(html);
        await Assert.That(first.Success).IsTrue();
        using var stream = typeof(App).Assembly.GetManifestResourceStream("GuestRegistrationStatusScript")!;
        using var reader = new StreamReader(stream);
        await Assert.That(first.Groups[2].Value).IsEqualTo(await reader.ReadToEndAsync(Cancellation));
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single()).Contains("'nonce-" + WebUtility.HtmlDecode(first.Groups[1].Value) + "'");
        await Assert.That(BaseHref().Match(html).Groups[1].Value).IsEqualTo(pathBase + "/");
        // The server cannot receive a fragment, and query input must not become bearer markup either.
        await Assert.That(html).DoesNotContain(token);
    }

    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private == true && response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    [GeneratedRegex("<head(?:\\s[^>]*)?>\\s*<script\\b(?![^>]*\\bsrc=)(?=[^>]*\\bnonce=\"([^\"]+)\")[^>]*>([\\s\\S]*?)</script>")]
    private static partial Regex FirstHeadScript();
    [GeneratedRegex("<base href=\"([^\"]+)\"")]
    private static partial Regex BaseHref();

    private sealed class Upstream(WebApplication app) : IAsyncDisposable
    {
        public string Address => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        public string? Capability { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? Referrer { get; private set; }
        public int RequestCount { get; private set; }
        public string? Method { get; private set; }
        public string? Body { get; private set; }
        public List<string> PrivilegedHeaders { get; } = [];
        public static async Task<Upstream> StartAsync(int status)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var upstream = new Upstream(app);
            app.Map("/{**path}", async (HttpContext context) =>
            {
                upstream.RequestCount++;
                upstream.Method = context.Request.Method;
                using var body = new StreamReader(context.Request.Body);
                upstream.Body = await body.ReadToEndAsync(context.RequestAborted);
                foreach (string header in new[] { "Authorization", "X-API-Key", "X-Setup-Secret", "X-Tenant-Id", "X-Tenant-Slug", "X-Support-Access-Session-Id" })
                    if (context.Request.Headers.ContainsKey(header)) upstream.PrivilegedHeaders.Add(header);
                upstream.Capability = context.Request.Headers["X-Registration-Order-Capability"].FirstOrDefault();
                upstream.PathAndQuery = context.Request.Path + context.Request.QueryString;
                upstream.Referrer = context.Request.Headers.Referer.FirstOrDefault();
                context.Response.Headers.CacheControl = "public, max-age=3600";
                context.Response.Headers["Referrer-Policy"] = "unsafe-url";
                context.Response.StatusCode = status;
                context.Response.ContentType = status == 200 ? "application/hal+json" : "application/problem+json";
                if (status != 204) await context.Response.WriteAsync("{}", context.RequestAborted);
            });
            await app.StartAsync(Cancellation);
            return upstream;
        }
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
