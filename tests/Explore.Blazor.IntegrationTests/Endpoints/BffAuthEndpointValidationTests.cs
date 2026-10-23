using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Constants;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Event.Web.BffHosting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Explore.Blazor.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffAuthEndpointValidationTests
{
    [Test]
    public async Task AuthProviders_WhenSchemeManagerThrows_ReturnsSafeProblemWithoutRawProviderError()
    {
        using var factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDynamicAuthSchemeManager>();
                services.AddSingleton<IDynamicAuthSchemeManager>(new ThrowingAuthSchemeManager());
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var response = await client.GetAsync("/auth/providers");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Authentication providers could not be resolved.");
        await Assert.That(body).Contains("auth_provider_resolution_failed");
        await Assert.That(body).Contains("correlationId");
        await Assert.That(body).DoesNotContain("raw provider failure");
        await Assert.That(body).DoesNotContain("refresh_token");
        await Assert.That(body).DoesNotContain("secretLen");
        await Assert.That(body).DoesNotContain("islamu-event-blazor");
    }

    [Test]
    public async Task RetainedKeycloakSchemeIsHiddenFromNewLoginWhenLocalIsPrimary()
    {
        using var factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDynamicAuthSchemeManager>();
                services.AddSingleton<IDynamicAuthSchemeManager>(
                    new RetainedKeycloakSchemeManager());
                var readiness = Substitute.For<IBffProviderReadinessService>();
                readiness.IsProviderReadyAsync(
                        AuthSchemeNames.Keycloak,
                        Arg.Any<CancellationToken>())
                    .Returns(true);
                readiness.MapSchemeToProviderQueryValue(AuthSchemeNames.Keycloak)
                    .Returns("keycloak");
                services.RemoveAll<IBffProviderReadinessService>();
                services.AddSingleton(readiness);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var providers = await client.GetAsync("/auth/providers");
        string body = await providers.Content.ReadAsStringAsync();
        using var challenge = await client.GetAsync(
            "/auth/challenge?provider=keycloak&returnUrl=/dashboard");

        await Assert.That(providers.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("\"primaryProvider\":\"local\"");
        await Assert.That(body).Contains("\"name\":\"local\"");
        await Assert.That(body).DoesNotContain("\"name\":\"keycloak\"");
        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(challenge.Headers.Location?.ToString()).Contains("/login");
    }

    [Test]
    public async Task AtprotoPrimaryReturnsOnlyTheReadyAtprotoProvider()
    {
        using var factory = new BlazorBffWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IDynamicAuthSchemeManager>();
                    services.AddSingleton<IDynamicAuthSchemeManager>(
                        new AtprotoPrimarySchemeManager());
                    var readiness =
                        Substitute.For<IBffProviderReadinessService>();
                    readiness.IsProviderReadyAsync(
                            Arg.Any<string>(),
                            Arg.Any<CancellationToken>())
                        .Returns(true);
                    readiness.HasMinimalProviderConfig(
                            Arg.Any<string>())
                        .Returns(true);
                    readiness.MapSchemeToProviderQueryValue(
                            AuthSchemeNames.Atproto)
                        .Returns("atproto");
                    readiness.MapSchemeToProviderQueryValue(
                            AuthSchemeNames.Keycloak)
                        .Returns("keycloak");
                    readiness.MapSchemeToProviderQueryValue(
                            AuthSchemeNames.Google)
                        .Returns("google");
                    services.RemoveAll<IBffProviderReadinessService>();
                    services.AddSingleton(readiness);
                });
            });
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using HttpResponseMessage response =
            await client.GetAsync("/auth/providers");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("\"primaryProvider\":\"atproto\"");
        await Assert.That(body).Contains("\"name\":\"atproto\"");
        await Assert.That(body).Contains("\"type\":\"handle_input\"");
        await Assert.That(body).DoesNotContain("\"name\":\"local\"");
        await Assert.That(body).DoesNotContain("\"name\":\"keycloak\"");
        await Assert.That(body).DoesNotContain("\"name\":\"google\"");
    }

    [Test]
    public async Task AtprotoDeploymentPrimaryRegistersSchemeBeforeApiConfigurationIsAvailable()
    {
        var schemes = new AuthenticationSchemeProvider(
            Options.Create(new AuthenticationOptions()));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = "atproto",
                ["Authentication:AtprotoLoginEnabled"] = "true",
                ["Atproto:PublicUrl"] = "https://events.example.test"
            })
            .Build();
        var manager = new DynamicAuthSchemeManager(
            schemes,
            new OptionsCache<OpenIdConnectOptions>(),
            Substitute.For<IServiceScopeFactory>(),
            configuration,
            new EphemeralDataProtectionProvider(),
            Substitute.For<IEventBffOidcOptionsFactory>(),
            Substitute.For<ILogger<DynamicAuthSchemeManager>>());

        await manager.InitializeAsync();

        await Assert.That(
                await manager.GetRegisteredProviderSchemesAsync())
            .Contains(AuthSchemeNames.Atproto);
    }

    [Test]
    [Arguments("keycloak", "missing", true, false)]
    [Arguments("keycloak", "whitespace", true, false)]
    [Arguments("keycloak", "present", true, false)]
    [Arguments("google", "missing", true, false)]
    [Arguments("google", "whitespace", true, false)]
    [Arguments("google", "present", true, false)]
    [Arguments("keycloak", "missing", false, false)]
    [Arguments("keycloak", "whitespace", false, false)]
    [Arguments("keycloak", "present", false, false)]
    [Arguments("keycloak", "present", true, true)]
    [Arguments("google", "present", true, true)]
    public async Task ConfidentialOidcColdStartRequiresAcquiredSecret(
        string provider, string secretState, bool explicitPrimary, bool occupied)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var sink = new ConfigurationSink();
        using var serilog = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext()
            .WithBffDiagnosticConfidentiality().WriteTo.Sink(sink).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        string? secret = secretState switch
        {
            "missing" => null,
            "whitespace" => " ",
            "present" => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            _ => throw new ArgumentOutOfRangeException(nameof(secretState))
        };
        bool isKeycloak = provider == "keycloak";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = explicitPrimary ? isKeycloak ? "keycloak" : "local" : null,
                ["Keycloak:Authority"] = isKeycloak ? $"https://identity.example.test/realms/{canary}" : null,
                ["Keycloak:ClientId"] = isKeycloak ? canary : null,
                ["Keycloak:ClientSecret"] = isKeycloak ? secret : null,
                ["Google:ClientId"] = isKeycloak ? null : canary,
                ["Google:ClientSecret"] = isKeycloak ? null : secret
            })
            .Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName = "Testing";
        var schemes = new AuthenticationSchemeProvider(
            Options.Create(new AuthenticationOptions()));
        string schemeName = isKeycloak ? AuthSchemeNames.Keycloak : AuthSchemeNames.Google;
        var optionsCache = new OptionsCache<OpenIdConnectOptions>();
        if (occupied)
        {
            schemes.TryAddScheme(new AuthenticationScheme(schemeName, schemeName, typeof(OpenIdConnectHandler)));
            optionsCache.TryAdd(schemeName, new OpenIdConnectOptions());
        }
        var receipts = new Explore.Blazor.Configuration.AuthenticationCredentialAcquisitionReceipts();
        var consumer = isKeycloak
            ? Explore.Blazor.Configuration.AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc
            : Explore.Blazor.Configuration.AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc;
        if (secretState == "present")
            receipts.RecordAcquired(consumer,
                Explore.Blazor.Configuration.AuthenticationCredentialAcquisitionReceipts.Authority.Environment);
        using var manager = new DynamicAuthSchemeManager(
            schemes,
            optionsCache,
            Substitute.For<IServiceScopeFactory>(),
            configuration,
            new EphemeralDataProtectionProvider(),
            new EventBffOidcOptionsFactory(
                environment, Substitute.For<ISafeAuthDiagnosticsPolicy>()),
            logs.CreateLogger<DynamicAuthSchemeManager>(), receipts);

        await manager.InitializeAsync();

        bool registered = (await manager.GetRegisteredProviderSchemesAsync()).Contains(schemeName);
        await Assert.That(registered).IsEqualTo(secretState == "present");
        await Assert.That(await schemes.GetSchemeAsync(schemeName) is not null)
            .IsEqualTo(secretState == "present");
        await Assert.That(manager.GetActivePrimaryProvider())
            .IsEqualTo(isKeycloak ? "keycloak" : "local");
        await Assert.That(receipts.Snapshot().Any(receipt => receipt.Installed))
            .IsEqualTo(secretState == "present" && !occupied);
        await Assert.That(sink.Values.Count > 0).IsTrue();
        foreach (string exported in sink.Values)
        {
            await Assert.That(exported).DoesNotContain(canary);
            if (!string.IsNullOrWhiteSpace(secret))
                await Assert.That(exported).DoesNotContain(secret);
        }
    }

    private sealed class ConfigurationSink : ILogEventSink
    {
        public List<string> Values { get; } = [];
        public void Emit(LogEvent entry) => Values.Add(entry.RenderMessage() + entry.Exception?.ToString()
            + string.Join(' ', entry.Properties.Select(property => property.Key + property.Value)));
    }

    [Test]
    public async Task AuthStatus_WithBrowserAuthorizationHeader_DoesNotAuthenticateOrEchoBearerToken()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        const string browserToken = "browser-supplied-access-token";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", browserToken);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("\"isAuthenticated\":false");
        await Assert.That(body).DoesNotContain(browserToken);
        await Assert.That(body).DoesNotContain("Bearer");
    }

    [Test]
    public async Task RefreshSchemes_WithoutAntiforgeryHeader_ReturnsBadRequest()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var response = await client.PostAsync("/bff/auth/refresh-schemes", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task RefreshSession_WithoutAntiforgeryHeader_ReturnsBadRequest()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid(), "Refresh Tester"));

        using var response = await client.PostAsync("/bff/auth/refresh-session", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    [Arguments(null)]
    [Arguments("malformed-self-call-token")]
    public async Task InternalRefreshSession_WithoutExactSelfCallToken_ReturnsForbiddenBeforeRefresh(string? token)
    {
        var selfCallTokens = new ExactSelfCallTokenService();
        var refresh = new RecordingSessionRefreshService();
        using var factory = CreateInternalRefreshFactory(selfCallTokens, refresh);
        using var client = CreateAuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/auth/refresh-session/internal");
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation(BffSelfCallHeaders.Token, token);
        }

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(selfCallTokens.ValidationCount).IsEqualTo(1);
        await Assert.That(refresh.RefreshCount).IsEqualTo(0);
    }

    [Test]
    public async Task InternalRefreshSession_WithExactSelfCallToken_InvokesRefresh()
    {
        var selfCallTokens = new ExactSelfCallTokenService();
        var refresh = new RecordingSessionRefreshService();
        using var factory = CreateInternalRefreshFactory(selfCallTokens, refresh);
        using var client = CreateAuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/auth/refresh-session/internal");
        request.Headers.TryAddWithoutValidation(BffSelfCallHeaders.Token, ExactSelfCallTokenService.ExactToken);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(selfCallTokens.ValidationCount).IsEqualTo(1);
        await Assert.That(refresh.RefreshCount).IsEqualTo(1);
    }

    private static WebApplicationFactory<Program> CreateInternalRefreshFactory(
        IBffSelfCallTokenService selfCallTokens,
        IBffSessionRefreshService refresh) =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBffSelfCallTokenService>();
                services.AddSingleton(selfCallTokens);
                services.RemoveAll<IBffSessionRefreshService>();
                services.AddSingleton(refresh);
            });
        });

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid(), "Internal Refresh Tester"));
        return client;
    }

    private sealed class ThrowingAuthSchemeManager : IDynamicAuthSchemeManager
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task RefreshSchemesAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetRegisteredProviderSchemesAsync() =>
            throw new InvalidOperationException(
                "raw provider failure refresh_token=secret-token secretLen=24 clientId=islamu-event-blazor");

        public string GetActivePrimaryProvider() => "local";
    }

    private sealed class RetainedKeycloakSchemeManager : IDynamicAuthSchemeManager
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task RefreshSchemesAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetRegisteredProviderSchemesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([AuthSchemeNames.Keycloak]);

        public string GetActivePrimaryProvider() => "local";
    }

    private sealed class AtprotoPrimarySchemeManager
        : IDynamicAuthSchemeManager
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task RefreshSchemesAsync() =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>>
            GetRegisteredProviderSchemesAsync() =>
            Task.FromResult<IReadOnlyList<string>>(
            [
                AuthSchemeNames.Keycloak,
                AuthSchemeNames.Google,
                AuthSchemeNames.Atproto
            ]);

        public string GetActivePrimaryProvider() => "atproto";
    }

    private sealed class ExactSelfCallTokenService : IBffSelfCallTokenService
    {
        public const string ExactToken = "exact-self-call-token";
        public int ValidationCount { get; private set; }
        public string? Issue(HttpContext? httpContext, HttpRequestMessage outboundRequest) => ExactToken;

        public bool Validate(HttpContext httpContext)
        {
            ValidationCount++;
            return httpContext.Request.Headers[BffSelfCallHeaders.Token].Count == 1
                && string.Equals(
                    httpContext.Request.Headers[BffSelfCallHeaders.Token][0],
                    ExactToken,
                    StringComparison.Ordinal);
        }
    }

    private sealed class RecordingSessionRefreshService : IBffSessionRefreshService
    {
        public int RefreshCount { get; private set; }

        public Task<IResult> RefreshSessionAsync(HttpContext context, CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(Results.Ok() as IResult);
        }

        public Task RevokeAtprotoSessionAsync(
            HttpContext context,
            AuthenticateResult authentication,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void ClearCircuitTokenState(
            HttpContext context,
            ClaimsPrincipal? principal,
            Microsoft.Extensions.Logging.ILogger logger,
            string reason)
        {
        }
    }
}
