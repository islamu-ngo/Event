using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using Explore.Blazor.Hosting;
using CarpaNet.OAuth.Storage;
using Explore.Blazor.Authentication;
using Explore.Blazor.Constants;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Events;

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

    [Test]
    [Arguments("success")]
    [Arguments("status")]
    [Arguments("fields")]
    [Arguments("json")]
    [Arguments("transport")]
    [Arguments("options")]
    [Arguments("cancelled")]
    public async Task NativeOidcReadinessExcludesPrivateMetadataFromSerilogAndForwardedLogs(string response)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var sink = new ReadinessSink();
        using var exporter = new ReadinessExporter();
        using var serilog = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext()
            .WithBffDiagnosticConfidentiality().WriteTo.Sink(sink).CreateLogger();
        using var logs = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug).AddSerilog(serilog, dispose: false);
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = false;
                options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
            });
        });
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var peer = builder.Build();
        int requests = 0;
        peer.Run(async http =>
        {
            Interlocked.Increment(ref requests);
            if (response == "transport")
            {
                http.Abort();
                return;
            }
            http.Response.StatusCode = response == "status" ? 503 : 200;
            await http.Response.WriteAsync(response switch
            {
                "json" => canary,
                "fields" => JsonSerializer.Serialize(new { issuer = canary }),
                _ => JsonSerializer.Serialize(new
                {
                    issuer = canary,
                    authorization_endpoint = $"https://identity.example.test/{canary}",
                    token_endpoint = $"https://identity.example.test/{canary}/token"
                })
            }, http.RequestAborted);
        });
        await peer.StartAsync();
        string address = peer.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        var options = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        options.Get(AuthSchemeNames.Keycloak).Returns(_ => response == "options"
            ? throw new InvalidOperationException(canary)
            : new OpenIdConnectOptions
            {
                Authority = address,
                ClientId = canary,
                MetadataAddress = $"{address}/{canary}?private={canary}"
            });
        using var service = CreateService(optionsMonitor: options,
            logger: logs.CreateLogger<BffProviderReadinessService>());
        using var scope = logs.CreateLogger<BffProviderReadinessService>()
            .BeginScope(new Dictionary<string, object> { ["PrivateScope"] = canary });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (response == "cancelled") deadline.Cancel();
        var readiness = await service.GetProviderReadinessAsync(AuthSchemeNames.Keycloak, deadline.Token);
        await Assert.That(readiness.IsReady).IsEqualTo(response == "success");
        await Assert.That(readiness.FailureCode).IsEqualTo(response switch
        {
            "success" => null,
            "options" => "options_unavailable",
            _ => "metadata_unavailable"
        });
        await Assert.That(requests > 0).IsEqualTo(response is not ("options" or "cancelled"));
        await Assert.That(sink.Values.Count > 0).IsTrue();
        await Assert.That(exporter.Values.Count > 0).IsTrue();
        foreach (string value in sink.Values.Concat(exporter.Values))
            await Assert.That(value).DoesNotContain(canary);
        await peer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private BffProviderReadinessService CreateService(
        IDynamicAuthSchemeManager? schemeManager = null,
        IOptionsMonitor<OpenIdConnectOptions>? optionsMonitor = null,
        AtprotoOAuthClientFactory? atprotoFactory = null,
        ILogger<BffProviderReadinessService>? logger = null)
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
            logger ?? NullLogger<BffProviderReadinessService>.Instance,
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

    private sealed class ReadinessSink : ILogEventSink
    {
        public ConcurrentQueue<string> Values { get; } = new();
        public void Emit(LogEvent entry) => Values.Enqueue(entry.RenderMessage() + entry.Exception?.ToString()
            + string.Join(' ', entry.Properties.Select(property => property.Key + property.Value)));
    }

    private sealed class ReadinessExporter : BaseExporter<LogRecord>
    {
        public ConcurrentQueue<string> Values { get; } = new();
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                Values.Enqueue(record.FormattedMessage + record.Exception?.ToString());
                if (record.Attributes is { } attributes)
                    foreach (var attribute in attributes)
                        Values.Enqueue($"{attribute.Key}={attribute.Value}");
                record.ForEachScope((scope, values) =>
                {
                    foreach (var value in scope)
                        values.Enqueue($"{value.Key}={value.Value}");
                }, Values);
            }
            return ExportResult.Success;
        }
    }
}
