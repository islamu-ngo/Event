// ABOUTME: Focused tests for BFF auth provider readiness and scheme mapping behavior.
// ABOUTME: Keeps provider-selection logic covered after extraction from auth endpoints.

using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet.OAuth.Storage;
using Explore.Blazor.Authentication;
using Explore.Blazor.Constants;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffProviderReadinessServiceTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    public void Dispose() => cache.Dispose();
    [Test]
    public async Task ResolveProviderScheme_WithKnownProvider_ReturnsAuthSchemeName()
    {
        using var service = CreateService();

        var scheme = service.ResolveProviderScheme("Google");

        await Assert.That(scheme).IsEqualTo(AuthSchemeNames.Google);
    }

    [Test]
    public async Task MapSchemeToProviderQueryValue_WithKeycloak_ReturnsProviderValue()
    {
        using var service = CreateService();

        var provider = service.MapSchemeToProviderQueryValue(AuthSchemeNames.Keycloak);

        await Assert.That(provider).IsEqualTo("keycloak");
    }

    [Test]
    public async Task IsProviderReadyAsyncWithOmittedAtprotoFactoryFailsClosedWithoutOidcOptions()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        using var service = CreateService(optionsMonitor: optionsMonitor);

        var ready = await service.IsProviderReadyAsync(AuthSchemeNames.Atproto, CancellationToken.None);

        await Assert.That(ready).IsFalse();
        var result = await service.GetProviderReadinessAsync(AuthSchemeNames.Atproto, CancellationToken.None);
        await Assert.That(result.FailureCode).IsEqualTo("provider_not_configured");
        optionsMonitor.DidNotReceiveWithAnyArgs().Get(default!);
    }

    [Test]
    public async Task ConfiguredAtprotoFactoryDoesNotCertifyAnUnavailableOperationalStore()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var availability = Substitute.For<IServiceProviderIsService>();
        availability.IsService(typeof(IOAuthStateStore)).Returns(true);
        availability.IsService(typeof(IOAuthSessionStore)).Returns(true);
        var transportFactory = Substitute.For<IAtprotoOAuthTransportFactory>();
        using var factory = new AtprotoOAuthClientFactory(
            new AtprotoClientKeyProvider(Options.Create(new AtprotoClientKeyOptions
            {
                OAuthClientPrivateJwks = CreatePrivateJwks()
            })),
            Options.Create(new AtprotoAuthenticationOptions
            {
                PublicUrl = "https://events.example.com/",
                CallbackPath = "/signin-atproto"
            }),
            environment,
            availability,
            transportFactory);
        using var service = CreateService(optionsMonitor: optionsMonitor, atprotoFactory: factory);

        var readiness = await service.GetProviderReadinessAsync(
            AuthSchemeNames.Atproto,
            CancellationToken.None);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.FailureCode).IsEqualTo("state_store_unavailable");
        await Assert.That(service.HasMinimalProviderConfig(AuthSchemeNames.Atproto)).IsTrue();
        optionsMonitor.DidNotReceiveWithAnyArgs().Get(default!);
        transportFactory.DidNotReceiveWithAnyArgs().CreatePrimaryHandler(default!, default);
        transportFactory.DidNotReceiveWithAnyArgs().CreateDnsResolver();
    }

    [Test]
    public async Task IsProviderReadyAsync_WithInvalidGoogleClientId_ReturnsFalse()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        optionsMonitor.Get(AuthSchemeNames.Google).Returns(new OpenIdConnectOptions
        {
            ClientId = "not-a-google-client-id",
            ClientSecret = "present"
        });
        using var service = CreateService(optionsMonitor: optionsMonitor);

        var ready = await service.IsProviderReadyAsync(AuthSchemeNames.Google, CancellationToken.None);

        await Assert.That(ready).IsFalse();
    }

    [Test]
    public async Task HasMinimalProviderConfig_WithAuthorityAndClientId_ReturnsTrue()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        optionsMonitor.Get(AuthSchemeNames.Keycloak).Returns(new OpenIdConnectOptions
        {
            Authority = "https://idp.example.test/realms/islamu",
            ClientId = "islamu-event-blazor"
        });
        using var service = CreateService(optionsMonitor: optionsMonitor);

        var hasConfig = service.HasMinimalProviderConfig(AuthSchemeNames.Keycloak);

        await Assert.That(hasConfig).IsTrue();
    }

    [Test]
    public async Task ResolvePreferredProviderForDirectLoginAsync_WithNoReadyButtonProvider_ReturnsNull()
    {
        var schemeManager = Substitute.For<IDynamicAuthSchemeManager>();
        schemeManager.GetRegisteredProviderSchemesAsync().Returns([AuthSchemeNames.Atproto]);
        using var service = CreateService(schemeManager: schemeManager);

        var provider = await service.ResolvePreferredProviderForDirectLoginAsync(CancellationToken.None);

        await Assert.That(provider).IsNull();
    }

    private BffProviderReadinessService CreateService(
        IDynamicAuthSchemeManager? schemeManager = null,
        IOptionsMonitor<OpenIdConnectOptions>? optionsMonitor = null,
        AtprotoOAuthClientFactory? atprotoFactory = null)
    {
        schemeManager ??= Substitute.For<IDynamicAuthSchemeManager>();
        optionsMonitor ??= Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(_ => throw new HttpRequestException("Unavailable transport."));
        var keys = new AtprotoClientKeyProvider(Options.Create(new AtprotoClientKeyOptions { OAuthClientPrivateJwks = CreatePrivateJwks() }));
        var transientStore = new ApiBackedAtprotoTransientStore(clients,
            new AtprotoTransientAssertionService(keys, TimeProvider.System), TimeProvider.System);

        return new BffProviderReadinessService(
            schemeManager,
            optionsMonitor,
            environment,
            NullLogger<BffProviderReadinessService>.Instance,
            new AtprotoAuthenticationMetrics(),
            transientStore,
            cache,
            TimeProvider.System,
            atprotoFactory);
    }

    private static string CreatePrivateJwks()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Encode(parameters.Q.X!),
                    y = Encode(parameters.Q.Y!),
                    d = Encode(parameters.D!),
                    kid = "active",
                    use = "sig",
                    alg = "ES256",
                    status = "active"
                }
            }
        });
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
