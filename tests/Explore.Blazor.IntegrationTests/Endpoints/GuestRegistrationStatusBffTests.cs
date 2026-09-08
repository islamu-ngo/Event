// ABOUTME: Exercises the native BFF proxy and initial host document for private guest status bookmarks.
// ABOUTME: Guards first-head inline transport, PathBase, and privacy headers against downstream overwrite.

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
        public static async Task<Upstream> StartAsync(int status)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var upstream = new Upstream(app);
            app.Map("/{**path}", async (HttpContext context) =>
            {
                upstream.Capability = context.Request.Headers["X-Registration-Order-Capability"].FirstOrDefault();
                upstream.PathAndQuery = context.Request.Path + context.Request.QueryString;
                upstream.Referrer = context.Request.Headers.Referer.FirstOrDefault();
                context.Response.Headers.CacheControl = "public, max-age=3600";
                context.Response.Headers["Referrer-Policy"] = "unsafe-url";
                context.Response.StatusCode = status;
                context.Response.ContentType = status == 200 ? "application/hal+json" : "application/problem+json";
                await context.Response.WriteAsync("{}", context.RequestAborted);
            });
            await app.StartAsync(Cancellation);
            return upstream;
        }
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
