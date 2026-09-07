using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core;

namespace Explore.Blazor.IntegrationTests.Endpoints;

/// <summary>
/// Verifies the Blazor BFF's behavior when no Keycloak configuration is present.
/// The DynamicAuthSchemeManager should find no env vars, register no OIDC schemes,
/// and the BFF should start and serve pages normally with no authentication provider.
/// </summary>
[Category(BffTestCategories.Security)]
[NotInParallel("BlazorBootstrapEnvironment")]
public class BffNoKeycloakResilienceTests : IAsyncDisposable
{
    private readonly NoKeycloakBlazorBffWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BffNoKeycloakResilienceTests()
    {
        _factory = new NoKeycloakBlazorBffWebApplicationFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    #region BFF Starts Without Keycloak

    [Test]
    public async Task Bff_StartsWithoutKeycloak()
    {
        await Assert.That(_factory).IsNotNull().Because("the BFF factory must build successfully without Keycloak config");
        await Assert.That(_client).IsNotNull().Because("the HTTP client must be creatable without OIDC errors");
    }

    #endregion

    #region Auth Endpoints — Graceful Degradation

    [Test]
    public async Task AuthStatus_ReturnsNotAuthenticated()
    {
        var response = await _client.GetAsync("/auth/status");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuthStatusPayload>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.IsAuthenticated).IsFalse().Because("without any auth provider, the user is always not authenticated");
    }

    [Test]
    public async Task Challenge_RedirectsToLoginPage_NoProvider()
    {
        var response = await _client.GetAsync("/auth/challenge?returnUrl=/dashboard");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect).Because("challenge should still redirect even without providers");

        var location = response.Headers.Location?.ToString();
        await Assert.That(string.IsNullOrEmpty(location)).IsFalse();

        await Assert.That(location).Contains("/login").Because("without any registered provider, challenge should redirect to the login page " +
        "for provider selection (which will show an empty list)");
    }

    [Test]
    public async Task Providers_ReturnsLocalIdentityAsDefaultPrimaryProvider()
    {
        var response = await _client.GetAsync("/auth/providers");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).Contains("providers").Because("the providers endpoint should return a valid response even when empty");

        var payload = await response.Content.ReadFromJsonAsync<ProvidersPayload>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.PrimaryProvider).IsEqualTo("local");
        await Assert.That(payload.Providers).IsNotNull();
        await Assert.That(payload.Providers!.Select(provider => provider.Name))
            .IsEquivalentTo(["local"]);
    }

    [Test]
    public async Task Signout_StillWorks()
    {
        var response = await _client.GetAsync("/auth/signout?returnUrl=/");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Redirect).Because("signout should work even for anonymous users without any provider configured");
    }

    #endregion

    #region Static Pages Accessible

    [Test]
    public async Task StaticPages_AreAccessible()
    {
        var response = await _client.GetAsync("/");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK).Because("static pages should be accessible regardless of auth configuration");
    }

    /// <summary>
    /// Serving the shell anonymously without an identity provider must not open protected
    /// resources. Routes are discovered from the live endpoint table so a newly added protected
    /// route is covered automatically instead of drifting away from a hardcoded list.
    /// </summary>
    [Test]
    public async Task ProtectedResources_StayFailClosedForAnonymousCallers()
    {
        Microsoft.AspNetCore.Routing.RouteEndpoint[] protectedReadRoutes = _factory.Services
            .GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(endpoint =>
                endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Count > 0
                && endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                .Contains(Microsoft.AspNetCore.Http.HttpMethods.Get, StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();

        var failures = new List<string>();

        foreach (var endpoint in protectedReadRoutes)
        {
            var route = System.Text.RegularExpressions.Regex.Replace(
                endpoint.RoutePattern.RawText ?? string.Empty,
                @"\{[^}]+\}",
                _ => Guid.CreateVersion7().ToString());

            using var response = await _client.GetAsync(route);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                failures.Add($"GET {route} => 200 OK");
            }

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(cookie => cookie.Contains(".AspNetCore.Cookies", StringComparison.Ordinal)))
            {
                failures.Add($"GET {route} issued an authentication cookie");
            }
        }

        await Assert.That(protectedReadRoutes).IsNotEmpty()
            .Because("the invariant is vacuous if no protected route is discovered");
        await Assert.That(failures).IsEmpty()
            .Because("anonymous callers must gain neither a session nor administrative authority when no identity provider is configured");
    }

    [Test]
    public async Task StaticPages_CarryContentSecurityPolicyHeader()
    {
        var response = await _client.GetAsync("/");

        await Assert.That(response.Headers.TryGetValues("Content-Security-Policy", out var values)).IsTrue().Because("BFF HTML responses must carry the launch CSP header");

        var csp = await Assert.That(values).HasSingleItem();
        await Assert.That(csp).Contains("default-src 'self'");
        await Assert.That(csp).Contains("script-src 'self' 'wasm-unsafe-eval'");
        await Assert.That(csp).Contains("img-src 'self' data: https: blob:");
        await Assert.That(csp).Contains("connect-src 'self' https: http: ws: wss:");
        await Assert.That(csp).Contains("font-src 'self' https://fonts.gstatic.com");
        await Assert.That(csp).Contains("frame-ancestors 'none'");
        await Assert.That(csp).Contains("base-uri 'self'");
        await Assert.That(csp).Contains("object-src 'none'");
        await Assert.That(csp).Contains("form-action 'self'");

        var body = await response.Content.ReadAsStringAsync();
        var nonceStart = csp.IndexOf("'nonce-", StringComparison.Ordinal);
        await Assert.That(nonceStart).IsGreaterThanOrEqualTo(0);
        nonceStart += "'nonce-".Length;
        var nonceEnd = csp.IndexOf('\'', nonceStart);
        await Assert.That(nonceEnd).IsGreaterThan(nonceStart);
        var nonce = csp[nonceStart..nonceEnd];

        await Assert.That(System.Net.WebUtility.HtmlDecode(body)).Contains($"<script type=\"importmap\" nonce=\"{nonce}\">");
        await Assert.That(body).DoesNotContain("http-equiv=\"Content-Security-Policy\"");
    }

    [Test]
    public async Task LaunchRoutes_CarryBrowserSecurityHeaders()
    {
        string[] paths = ["/", "/errors/404", "/css/layers.css"];

        foreach (var path in paths)
        {
            var response = await _client.GetAsync(path);

            await Assert.That(response.StatusCode).IsNotEqualTo(System.Net.HttpStatusCode.InternalServerError);
            await AssertBrowserSecurityHeaders(response, path);
        }
    }

    [Test]
    public async Task AppShell_LinksWhiteLabelManifest()
    {
        var response = await _client.GetAsync("/");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("rel=\"manifest\" href=\"manifest.webmanifest\"");
        await Assert.That(body).Contains("name=\"theme-color\" content=\"#2563eb\"");
        await Assert.That(body).DoesNotContain("Icon_landingpage.png");
    }

    [Test]
    public async Task ManifestWebManifest_ReturnsDbBackedWhiteLabelInstallMetadata()
    {
        var apiClient = Substitute.For<IPublicExperienceClient>();
        apiClient.GetPublicExperienceShellAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PublicExperienceShellDto
            {
                Home = new PublicExperienceHomeDto
                {
                    BrandDisplayName = "Community Events",
                    BrandLogoUrl = "https://cdn.example.test/logo.png",
                    BrandFaviconUrl = "https://cdn.example.test/favicon.svg"
                }
            }));
        await using var factory = new NoKeycloakBlazorBffWebApplicationFactory(configureServices: services =>
        {
            services.RemoveAll<IPublicExperienceClient>();
            services.AddSingleton(apiClient);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var response = await client.GetAsync("/manifest.webmanifest");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("ISLAMU");
        await Assert.That(body).DoesNotContain("Icon_landingpage.png");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("Community Events");
        await Assert.That(root.GetProperty("short_name").GetString()).IsEqualTo("Community");
        await Assert.That(root.GetProperty("description").GetString()).IsEqualTo("Discover and register for events.");
        await Assert.That(root.GetProperty("start_url").GetString()).IsEqualTo("/");
        await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("/");
        await Assert.That(root.GetProperty("display").GetString()).IsEqualTo("standalone");
        await Assert.That(root.GetProperty("theme_color").GetString()).IsEqualTo("#2563eb");
        await Assert.That(root.GetProperty("background_color").GetString()).IsEqualTo("#ffffff");

        var icons = root.GetProperty("icons").EnumerateArray().ToArray();
        await Assert.That(icons).Contains(icon =>
            icon.GetProperty("src").GetString() == "https://cdn.example.test/favicon.svg" &&
            icon.GetProperty("sizes").GetString() == "any" &&
            icon.GetProperty("type").GetString() == "image/svg+xml");
        await Assert.That(icons).Contains(icon =>
            icon.GetProperty("src").GetString() == "https://cdn.example.test/logo.png" &&
            icon.GetProperty("sizes").GetString() == "any" &&
            icon.GetProperty("type").GetString() == "image/png");
    }

    [Test]
    public async Task RobotsTxt_WhenNonProduction_DisallowsCrawlers()
    {
        var response = await _client.GetAsync("/robots.txt");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("User-agent: *");
        await Assert.That(body).Contains("Disallow: /");
        await Assert.That(body).DoesNotContain("Sitemap:");
    }

    [Test]
    public async Task RobotsTxt_WhenProduction_IgnoresDirectForwardedHost()
    {
        await using var factory = new NoKeycloakBlazorBffWebApplicationFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "events.example.test");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("User-agent: *");
        await Assert.That(body).Contains("Allow: /");
        await Assert.That(body).Contains($"Sitemap: https://{client.BaseAddress!.Authority}/sitemap.xml");
        await Assert.That(body).DoesNotContain("events.example.test");
    }

    #endregion

    private static async Task AssertBrowserSecurityHeaders(HttpResponseMessage response, string path)
    {
        await Assert.That(response.Headers.Contains("Content-Security-Policy")).IsTrue().Because(path);
        await Assert.That(await Assert.That(response.Headers.GetValues("X-Frame-Options")).HasSingleItem()).IsEqualTo("DENY").Because(path);
        await Assert.That(await Assert.That(response.Headers.GetValues("X-Content-Type-Options")).HasSingleItem()).IsEqualTo("nosniff").Because(path);
        await Assert.That(await Assert.That(response.Headers.GetValues("Referrer-Policy")).HasSingleItem()).IsEqualTo("strict-origin-when-cross-origin").Because(path);
        await Assert.That(await Assert.That(response.Headers.GetValues("Permissions-Policy")).HasSingleItem())
            .IsEqualTo("camera=(), microphone=(), geolocation=(self), payment=()").Because(path);
    }

    private sealed class AuthStatusPayload
    {
        public bool IsAuthenticated { get; set; }
        public string? Name { get; set; }
    }

    private sealed class ProvidersPayload
    {
        public string? PrimaryProvider { get; set; }
        public List<ProviderPayload>? Providers { get; set; }
    }

    private sealed class ProviderPayload
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// BFF WebApplicationFactory with NO Keycloak configuration.
    /// The DynamicAuthSchemeManager will find no env vars and register no OIDC schemes.
    /// </summary>
    private sealed class NoKeycloakBlazorBffWebApplicationFactory : WebApplicationFactory<Program>
    {
        private static readonly IReadOnlyDictionary<string, string?> BootstrapEnvironmentOverrides =
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["SecretProvider__Provider"] = "Environment",
                ["Keycloak__Authority"] = string.Empty,
                ["Keycloak__MetadataAddress"] = string.Empty,
                ["Keycloak__Realm"] = string.Empty,
                ["Keycloak__ClientId"] = string.Empty,
                ["Keycloak__ClientSecret"] = string.Empty,
                ["ConnectionStrings__cache"] = "localhost:6379,abortConnect=false,connectTimeout=100"
            };

        private readonly Dictionary<string, string?> _originalEnvironmentValues = new(StringComparer.Ordinal);

        private readonly string _environmentName;
        private readonly Action<IServiceCollection>? _configureServices;

        public NoKeycloakBlazorBffWebApplicationFactory(
            string environmentName = "Development",
            Action<IServiceCollection>? configureServices = null)
        {
            _environmentName = environmentName;
            _configureServices = configureServices;

            foreach (var (key, value) in BootstrapEnvironmentOverrides)
            {
                _originalEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environmentName);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var testConfig = new Dictionary<string, string?>
                {
                    ["SecretProvider:Provider"] = "Environment",
                    ["Keycloak:Authority"] = string.Empty,
                    ["Keycloak:MetadataAddress"] = string.Empty,
                    ["Keycloak:Realm"] = string.Empty,
                    ["Keycloak:ClientId"] = string.Empty,
                    ["Keycloak:ClientSecret"] = string.Empty,
                    ["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false,connectTimeout=100",
                    ["Deployment:Mode"] = "SingleTenant",
                    ["Deployment:DefaultTenantId"] = "018e4e5c-7f00-7000-8000-000000000001",
                    ["ExploreApi:BaseUrl"] = "http://localhost:9999/",
                };

                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();

                services.RemoveAll<IExploreApiReadinessProbe>();
                var readiness =
                    Substitute.For<IExploreApiReadinessProbe>();
                readiness.EnsureReadyAsync(Arg.Any<CancellationToken>())
                    .Returns(Task.CompletedTask);
                services.AddSingleton(readiness);

                services.RemoveAll<IBffResolverConfigurationProvider>();
                var mockResolverConfiguration = Substitute.For<IBffResolverConfigurationProvider>();
                mockResolverConfiguration.GetConfigurationAsync(Arg.Any<CancellationToken>())
                    .Returns(new ResolverConfigurationDto { PathEnabled = false });
                services.AddSingleton(mockResolverConfiguration);

                var mockOnboarding = NSubstitute.Substitute.For<Explore.Blazor.Services.IBffOnboardingStatusProvider>();
                mockOnboarding.GetStatusAsync(Arg.Any<CancellationToken>())
                    .Returns(new Explore.Blazor.Services.BffOnboardingStatus(
                        IsCompleted: true,
                        State: "Completed",
                        Mode: "Interactive",
                        Provider: null,
                        Generation: 0,
                        Disposition: Explore.Blazor.Services.BffOnboardingDisposition.Completed));
                services.RemoveAll<Explore.Blazor.Services.IBffOnboardingStatusProvider>();
                services.AddSingleton(mockOnboarding);

                var onboardingClient = Substitute.For<IInstanceOnboardingClient>();
                onboardingClient.GetInstanceOnboardingStatusAsync(
                        Arg.Any<string?>(),
                        Arg.Any<string?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new HalResourceOfInstanceOnboardingStatusDto
                    {
                        IsCompleted = true,
                        State = "Completed",
                        Mode = "Interactive",
                        Generation = 1,
                        SelectedDeploymentMode = "SingleTenant"
                    }));
                services.RemoveAll<IInstanceOnboardingClient>();
                services.AddSingleton(onboardingClient);

                _configureServices?.Invoke(services);
            });
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var (key, value) in _originalEnvironmentValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            base.Dispose(disposing);
        }
    }
}
