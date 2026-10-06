using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Contracts.Providers;
using Explore.Blazor.Client.Services;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace Explore.Blazor.IntegrationTests.Endpoints;

/// <summary>
/// Guards the host's emitted typography resources and delivered font bytes,
/// without replacing static asset routing or the browser security policy.
/// </summary>
public sealed partial class TypographyAssetContractTests : IAsyncDisposable
{
    private readonly TypographyFactory _factory = new();
    private readonly HttpClient _client;

    public TypographyAssetContractTests()
    {
        _client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
    }

    [Test]
    public async Task TypographyCspDoesNotAuthorizeExternalFonts()
    {
        using var response = await _client.GetAsync("/");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var directives = response.Headers.GetValues("Content-Security-Policy").Single()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(directives.Single(value =>
                value.StartsWith("font-src ", StringComparison.Ordinal)))
            .IsEqualTo("font-src 'self'");
        await Assert.That(directives.Single(value =>
                value.StartsWith("style-src ", StringComparison.Ordinal)))
            .IsEqualTo("style-src 'self' 'unsafe-inline'");
    }

    [Test]
    public async Task ShellDoesNotEmitThirdPartyTypographyResources()
    {
        using var response = await _client.GetAsync("/");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync();
        var resources = ResourceAttribute().Matches(document)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Select(value => new Uri(_client.BaseAddress!, value))
            .ToArray();

        await Assert.That(resources.Length).IsGreaterThan(0);
        await Assert.That(resources.Any(resource =>
            resource.Host.Equals("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
            || resource.Host.Equals("fonts.gstatic.com", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(resources.Any(resource =>
            resource.AbsolutePath.StartsWith("/css/fonts", StringComparison.Ordinal)
            && resource.Host == _client.BaseAddress!.Host)).IsTrue();
        foreach (Match stylesheet in StylesheetLink().Matches(document))
        {
            var stylesheetUri = new Uri(_client.BaseAddress!,
                WebUtility.HtmlDecode(stylesheet.Groups[1].Value));
            await Assert.That(stylesheetUri.GetLeftPart(UriPartial.Authority))
                .IsEqualTo(_client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        }
    }

    [Test]
    [Arguments("/css/fonts.css", "text/css")]
    [Arguments("/fonts/inter/InterVariable.woff2", "font/woff2")]
    public async Task LocalTypographyAssetsAreServedInsteadOfHtml(string path, string mediaType)
    {
        using var response = await _client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo(mediaType);
    }

    [Test]
    [Arguments(null, "InteractiveServer", "server")]
    [Arguments(null, "InteractiveWebAssembly", "webassembly")]
    [Arguments(null, "InteractiveAuto", "auto")]
    [Arguments("https://localhost/community/", "InteractiveServer", "server")]
    [Arguments("https://localhost/community/", "InteractiveWebAssembly", "webassembly")]
    [Arguments("https://localhost/community/", "InteractiveAuto", "auto")]
    public async Task LinkedStylesheetDeliversTheAdmittedLocalFont(
        string? publicBaseUrl, string renderMode, string descriptorType)
    {
        await using var factory = new TypographyFactory(publicBaseUrl, renderMode);
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri(publicBaseUrl ?? "http://localhost/"),
            AllowAutoRedirect = false
        });
        using var shell = await client.GetAsync(string.Empty);
        await Assert.That(shell.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var document = await shell.Content.ReadAsStringAsync();
        var rendererTypes = BlazorDescriptor().Matches(document)
            .Select(match =>
            {
                using var descriptor = JsonDocument.Parse(match.Groups[1].Value);
                return descriptor.RootElement.TryGetProperty("type", out var type)
                    ? type.GetString()
                    : null;
            })
            .OfType<string>()
            .ToArray();
        await Assert.That(rendererTypes).Contains(descriptorType);
        var baseHref = BaseHref().Match(document);
        await Assert.That(baseHref.Success).IsTrue();
        var documentBase = new Uri(client.BaseAddress!, WebUtility.HtmlDecode(baseHref.Groups[1].Value));
        await Assert.That(documentBase).IsEqualTo(client.BaseAddress);
        var stylesheetLinks = ResourceAttribute().Matches(document)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(value => value.StartsWith("css/fonts", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(stylesheetLinks.Length).IsEqualTo(1);
        var stylesheetUri = new Uri(documentBase, stylesheetLinks[0]);
        await Assert.That(stylesheetUri.GetLeftPart(UriPartial.Authority))
            .IsEqualTo(documentBase.GetLeftPart(UriPartial.Authority));

        using var stylesheet = await client.GetAsync(stylesheetUri);
        await Assert.That(stylesheet.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(stylesheet.Content.Headers.ContentType!.MediaType).IsEqualTo("text/css");
        var css = await stylesheet.Content.ReadAsStringAsync();
        var fontReference = FontUrl().Match(css);
        await Assert.That(fontReference.Success).IsTrue();
        var fontUri = new Uri(stylesheetUri, fontReference.Groups[1].Value);
        await Assert.That(fontUri.GetLeftPart(UriPartial.Authority))
            .IsEqualTo(documentBase.GetLeftPart(UriPartial.Authority));
        await Assert.That(fontUri.AbsolutePath)
            .StartsWith(client.BaseAddress!.AbsolutePath);

        using var font = await client.GetAsync(fontUri);
        await Assert.That(font.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(font.Content.Headers.ContentType!.MediaType).IsEqualTo("font/woff2");
        var bytes = await font.Content.ReadAsByteArrayAsync();
        await Assert.That(bytes.Length).IsEqualTo(352240);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)))
            .IsEqualTo("693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3");
        await Assert.That(shell.Headers.GetValues("Content-Security-Policy").Single())
            .Contains("font-src 'self'");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [GeneratedRegex("(?:href|src)\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceAttribute();

    [GeneratedRegex("""<base\b[^>]*\bhref=["']([^"']+)["'][^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex BaseHref();

    [GeneratedRegex("""<link\b(?=[^>]*\brel=["']stylesheet["'])[^>]*\bhref=["']([^"']+)["'][^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex StylesheetLink();

    [GeneratedRegex("""url\("([^"]+\.woff2)"\)""")]
    private static partial Regex FontUrl();

    [GeneratedRegex("""<!--Blazor:(.*?)-->""")]
    private static partial Regex BlazorDescriptor();

    private sealed class TypographyFactory(string? publicBaseUrl = null, string? renderMode = null)
        : BlazorBffWebApplicationFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (publicBaseUrl is not null)
            {
                builder.UseSetting("PublicBaseUrl", publicBaseUrl);
            }
            builder.ConfigureTestServices(services =>
            {
                if (renderMode is not null)
                {
                    services.RemoveAll<IRuntimeRenderPolicyService>();
                    var policy = Substitute.For<IRuntimeRenderPolicyService>();
                    policy.ResolveForPathAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                        .Returns(Task.FromResult(new RuntimeRenderPolicyDecision(
                            renderMode, PrerenderEnabled: true, RuntimeRouteGroup.PublicSeo)));
                    services.AddSingleton(policy);
                }
                services.RemoveAll<IExploreApiReadinessProbe>();
                var readiness = Substitute.For<IExploreApiReadinessProbe>();
                readiness.EnsureReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
                services.AddSingleton(readiness);
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                        handlerBuilder.PrimaryHandler = new UnavailableApiHandler()));
            });
        }
    }

    private sealed class UnavailableApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
