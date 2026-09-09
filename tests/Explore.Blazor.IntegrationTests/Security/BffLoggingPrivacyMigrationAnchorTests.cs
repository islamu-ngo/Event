using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Blazor.Authentication;
using Explore.Blazor.Client.Configuration;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace Explore.Blazor.IntegrationTests.Security;

[Category("MigrationAnchor")]
public sealed class BffLoggingPrivacyMigrationAnchorTests
{
    [Test]
    public async Task CircuitTokenStoreLogsNoRawOpaqueSubjectSessionOrToken()
    {
        using var capture = new DualLogCapture(typeof(CircuitTokenStore).FullName!);
        var subject = $"opaque-sub-{Guid.CreateVersion7():N}";
        var session = $"opaque-session-{Guid.CreateVersion7():N}";
        var token = Guid.CreateVersion7().ToString("N");
        var store = new CircuitTokenStore(capture.Factory.CreateLogger<CircuitTokenStore>());

        store.Store(subject, session, token);
        _ = store.Resolve(subject, session);

        await AssertPrivateAndBoundedAsync(capture, [subject, session, token]);
    }

    [Test]
    public async Task BffAdminClaimsTransformationLogsNoRawOpaqueSubjectSessionOrToken()
    {
        using var capture = new DualLogCapture(typeof(BffAdminClaimsTransformation).FullName!);
        var subject = $"opaque-sub-{Guid.CreateVersion7():N}";
        var session = $"opaque-session-{Guid.CreateVersion7():N}";
        var token = Guid.CreateVersion7().ToString("N");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject), new Claim("sid", session)
        ], "Cookies"));
        var readiness = Substitute.For<IBffOnboardingStatusProvider>();
        readiness.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new BffOnboardingStatus(
                true, "Completed", "Interactive", null, 0, BffOnboardingDisposition.Completed));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new BffAdminClaimsTransformation(
            Substitute.For<IHttpClientFactory>(), cache, readiness,
            capture.Factory.CreateLogger<BffAdminClaimsTransformation>());

        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
        _ = await service.EnrichPrincipalAsync(principal, properties);

        await AssertPrivateAndBoundedAsync(capture, [subject, session, token]);
    }

    [Test]
    public async Task BffSessionRefreshServiceLogsNoRawOpaqueSubjectSessionOrToken()
    {
        const string category = "Task41.SessionRefresh";
        using var capture = new DualLogCapture(category);
        var subject = $"opaque-sub-{Guid.CreateVersion7():N}";
        var session = $"opaque-session-{Guid.CreateVersion7():N}";
        var token = Guid.CreateVersion7().ToString("N");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject), new Claim("sid", session)
        ], "Cookies"));
        var context = new DefaultHttpContext { User = principal };
        var tokenStore = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var tokenService = new CircuitAccessTokenService(
            tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        tokenService.SetToken(token);
        var userContext = new CircuitUserContext();
        userContext.SetUserId(subject);
        userContext.SetSessionId(session);
        var cookieStore = new BffAuthCookieStore();
        cookieStore.SetCookieHeader($"bff={Guid.CreateVersion7():N}");
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(capture.Factory)
            .AddSingleton<ICircuitAccessTokenService>(tokenService).AddSingleton<ICircuitUserContext>(userContext)
            .AddSingleton<IBffAuthCookieStore>(cookieStore).AddSingleton<ICircuitTokenStore>(tokenStore)
            .BuildServiceProvider();
        context.RequestServices = services;
        var service = CreateSessionRefreshService();

        service.ClearCircuitTokenState(
            context, principal, capture.Factory.CreateLogger(category), "bounded_reason");

        await AssertPrivateAndBoundedAsync(capture, [subject, session, token]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [NotInParallel]
    public async Task TokenCircuitHandlerLogsNoRawOpaqueSubjectSessionTokenOrCookie(bool hasSession)
    {
        using var capture = new DualLogCapture(typeof(TokenCircuitHandler).FullName!);
        var subject = Guid.CreateVersion7().ToString("D");
        var session = $"opaque-session-{Guid.CreateVersion7():N}";
        var token = Guid.CreateVersion7().ToString("N");
        var cookie = $"bff={Guid.CreateVersion7():N}";
        var circuitCapture = new CircuitCaptureHandler();
        await using var baseFactory = new BlazorBffWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                capture.AddProviders(services);
                services.AddSingleton<CircuitHandler>(circuitCapture);
                var readiness = Substitute.For<IBffOnboardingStatusProvider>();
                readiness.GetStatusAsync(Arg.Any<CancellationToken>())
                    .Returns(new BffOnboardingStatus(
                        true, "Completed", "Interactive", null, 0, BffOnboardingDisposition.Completed));
                services.AddSingleton(new BffAdminClaimsTransformation(
                    Substitute.For<IHttpClientFactory>(),
                    Substitute.For<IMemoryCache>(),
                    readiness,
                    NullLogger<BffAdminClaimsTransformation>.Instance));
            });
        });
        var claims = new List<Claim> { new("sub", subject) };
        if (hasSession)
        {
            claims.Add(new Claim("sid", session));
        }
        string authHeader = EncodeTestClaims(claims
            .Select(claim => new TestAuthHandler.TestClaimDto(claim.Type, claim.Value))
            .ToArray());

        await OpenRealCircuitAsync(factory, authHeader, cookie);
        Circuit circuit = await circuitCapture.Opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"))
        };
        context.Items["AccessToken"] = token;
        context.Request.Headers.Cookie = cookie;
        using var requestScope = factory.Services.CreateScope();
        context.RequestServices = requestScope.ServiceProvider;
        var authenticationState = new ServerAuthenticationStateProvider();
        authenticationState.SetAuthenticationState(Task.FromResult(new AuthenticationState(context.User)));
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var accessor = new HttpContextAccessor { HttpContext = context };
        using var handler = new TokenCircuitHandler(
            httpContextAccessor: accessor,
            circuitAccessTokenService: new CircuitAccessTokenService(
                store, accessor, NullLogger<CircuitAccessTokenService>.Instance),
            circuitUserContext: new CircuitUserContext(),
            bffAuthCookieStore: new BffAuthCookieStore(),
            logger: capture.Factory.CreateLogger<TokenCircuitHandler>(),
            authenticationStateProvider: authenticationState,
            adminClaimsTransformation: requestScope.ServiceProvider.GetRequiredService<BffAdminClaimsTransformation>());

        await handler.OnCircuitOpenedAsync(circuit, CancellationToken.None);

        await AssertPrivateAndBoundedAsync(capture, [subject, session, token, cookie, circuit.Id]);
        foreach (var entries in new[] { capture.MicrosoftEntries, capture.SerilogEntries })
        {
            var presenceValues = entries
                .Where(entry => entry.Properties.ContainsKey("SessionPresent"))
                .Select(entry => entry.Properties["SessionPresent"])
                .ToArray();
            await Assert.That(presenceValues.Length).IsGreaterThan(0);
            await Assert.That(presenceValues.All(value => value == hasSession.ToString())).IsTrue();
        }
    }


    [Test]
    public async Task ForwardingHandlersLogOnlyCompiledRouteClassifications()
    {
        var rawApiPath = $"/api/private/{Guid.CreateVersion7():N}?secret={Guid.CreateVersion7():N}";
        var rawBffPath = $"/bff/private/{Guid.CreateVersion7():N}?token={Guid.CreateVersion7():N}";

        using (var accessCapture = new DualLogCapture(typeof(AccessTokenForwardingHandler).FullName!))
        {
            var context = new DefaultHttpContext();
            var accessor = new HttpContextAccessor { HttpContext = context };
            var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
            var tokenService = new CircuitAccessTokenService(
                store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
            var handler = new AccessTokenForwardingHandler(
                accessor,
                tokenService,
                new CircuitUserContext(),
                store,
                accessCapture.Factory.CreateLogger<AccessTokenForwardingHandler>())
            {
                InnerHandler = new TerminalHandler()
            };
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync($"https://example.test{rawApiPath}");

            await AssertPrivateAndBoundedAsync(accessCapture, [rawApiPath, "secret="]);
        }

        using (var cookieCapture = new DualLogCapture(typeof(BffCookieForwardingHandler).FullName!))
        {
            var cookieStore = new BffAuthCookieStore();
            cookieStore.SetCookieHeader($"bff={Guid.CreateVersion7():N}");
            var handler = new BffCookieForwardingHandler(
                cookieStore,
                cookieCapture.Factory.CreateLogger<BffCookieForwardingHandler>())
            {
                InnerHandler = new TerminalHandler()
            };
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync($"https://example.test{rawBffPath}");

            await AssertPrivateAndBoundedAsync(cookieCapture, [rawBffPath, "token="]);
        }
    }
    private static string EncodeTestClaims(IReadOnlyCollection<TestAuthHandler.TestClaimDto> claims) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));

    private static async Task OpenRealCircuitAsync(
        WebApplicationFactory<Program> factory,
        string authHeader,
        string cookie)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, authHeader);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        string html = await client.GetStringAsync("/");
        string descriptors = SerializeServerDescriptors(html);

        using var negotiateRequest = new HttpRequestMessage(
            HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        using HttpResponseMessage negotiateResponse = await client.SendAsync(negotiateRequest);
        negotiateResponse.EnsureSuccessStatusCode();
        using JsonDocument negotiate = JsonDocument.Parse(
            await negotiateResponse.Content.ReadAsStringAsync());
        string connectionToken = negotiate.RootElement.GetProperty("connectionToken").GetString()!;

        WebSocketClient webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
        {
            request.Headers[TestAuthHandler.AuthHeaderName] = authHeader;
            request.Headers.Cookie = cookie;
        };
        using WebSocket socket = await webSocketClient.ConnectAsync(
            new Uri($"ws://localhost/_blazor?id={Uri.EscapeDataString(connectionToken)}"),
            CancellationToken.None);
        byte[] handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"blazorpack\",\"version\":1}\u001e");
        await socket.SendAsync(handshake, WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReceiveMessageAsync(socket);

        IHubProtocol protocol = factory.Services.GetServices<IHubProtocol>()
            .Single(candidate => candidate.Name == "blazorpack");
        var invocation = new InvocationMessage(
            "task-4.1", "StartCircuit",
            ["http://localhost/", "http://localhost/", descriptors, string.Empty]);
        var writer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(invocation, writer);
        await socket.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, CancellationToken.None);
        using var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CompletionMessage? completion = null;
        while (completion is null)
        {
            byte[] response = await ReceiveMessageAsync(socket, completionTimeout.Token);
            var sequence = new ReadOnlySequence<byte>(response);
            while (protocol.TryParseMessage(ref sequence, TestInvocationBinder.Instance, out HubMessage? message))
            {
                if (message is CompletionMessage candidate && candidate.InvocationId == "task-4.1")
                {
                    completion = candidate;
                }
            }
        }
        await Assert.That(completion).IsNotNull().Because("StartCircuit must complete through the real Blazor hub");
        await Assert.That(completion!.Error).IsNull().Because("real circuit startup must not fail for a harness reason");
    }

    private static string SerializeServerDescriptors(string html)
    {
        var markers = new List<string>();
        foreach (Match match in Regex.Matches(
            html, "<!--Blazor:(?<marker>\\{.*?\\})-->", RegexOptions.CultureInvariant))
        {
            using JsonDocument marker = JsonDocument.Parse(match.Groups["marker"].Value);
            JsonElement root = marker.RootElement;
            if (root.TryGetProperty("type", out JsonElement type)
                && type.GetString() == "server"
                && root.TryGetProperty("descriptor", out JsonElement descriptor))
            {
                _ = descriptor.GetString();
                markers.Add(match.Groups["marker"].Value);
            }
        }

        return $"[{string.Join(',', markers)}]";
    }

    private static async Task<byte[]> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken = default)
    {
        var writer = new ArrayBufferWriter<byte>();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            writer.Write(buffer.AsSpan(0, result.Count));
        }
        while (!result.EndOfMessage);
        return writer.WrittenSpan.ToArray();
    }


    private sealed class TestInvocationBinder : IInvocationBinder
    {
        public static TestInvocationBinder Instance { get; } = new();
        public IReadOnlyList<Type> GetParameterTypes(string methodName) => [];
        public Type GetReturnType(string invocationId) => typeof(string);
        public Type GetStreamItemType(string streamId) => typeof(object);
    }

    private sealed class CircuitCaptureHandler : CircuitHandler
    {
        public TaskCompletionSource<Circuit> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            Opened.TrySetResult(circuit);
            return Task.CompletedTask;
        }
    }
    private static async Task AssertPrivateAndBoundedAsync(
        DualLogCapture capture,
        IReadOnlyCollection<string> sensitiveValues)
    {
        CapturedLog[] microsoft = capture.MicrosoftEntries.ToArray();
        CapturedLog[] serilog = capture.SerilogEntries.ToArray();
        bool microsoftPrivate = sensitiveValues.All(sensitive =>
            microsoft.All(entry => !entry.Flattened.Contains(sensitive, StringComparison.Ordinal)));
        bool serilogPrivate = sensitiveValues.All(sensitive =>
            serilog.All(entry => !entry.Flattened.Contains(sensitive, StringComparison.Ordinal)));
        CapturedLog[] allEntries = [.. microsoft, .. serilog];
        string[] forbiddenPropertyNames =
            ["CircuitId", "Length", "Path", "UserId", "SessionId", "ClaimType", "Timestamp"];
        bool boundedProperties = allEntries.All(entry =>
            entry.Properties.Keys.All(key =>
                !forbiddenPropertyNames.Contains(key, StringComparer.OrdinalIgnoreCase)));
        bool timestampFree = allEntries.All(entry =>
            !Regex.IsMatch(
                entry.Flattened,
                "\\b\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}",
                RegexOptions.CultureInvariant));

        using (Assert.Multiple())
        {
            await Assert.That(microsoft.Length).IsGreaterThan(0)
                .Because("Microsoft logging must capture the target surface");
            await Assert.That(serilog.Length).IsGreaterThan(0)
                .Because("isolated Serilog must capture the target surface");
            await Assert.That(microsoft.Length).IsLessThanOrEqualTo(8)
                .Because("Microsoft logging records must remain bounded");
            await Assert.That(serilog.Length).IsLessThanOrEqualTo(8)
                .Because("isolated Serilog records must remain bounded");
            await Assert.That(microsoftPrivate).IsTrue()
                .Because("Microsoft logging leaked a raw BFF subject, session, token, or cookie value");
            await Assert.That(serilogPrivate).IsTrue()
                .Because("isolated Serilog leaked a raw BFF subject, session, token, or cookie value");
            await Assert.That(boundedProperties).IsTrue()
                .Because("governed BFF logs carried an identifier, path, timestamp, or length property");
            await Assert.That(timestampFree).IsTrue()
                .Because("governed BFF logs carried a timestamp-bearing reason");
        }
    }

    private static BffSessionRefreshService CreateSessionRefreshService()
    {
        var readiness = Substitute.For<IBffOnboardingStatusProvider>();
        var cache = Substitute.For<IMemoryCache>();
        var admin = new BffAdminClaimsTransformation(
            Substitute.For<IHttpClientFactory>(), cache, readiness,
            NullLogger<BffAdminClaimsTransformation>.Instance);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return new BffSessionRefreshService(
            admin,
            new BffAccessTokenAssessmentService(),
            Substitute.For<IHttpClientFactory>(),
            new AtprotoBootstrapAssertionService(CreateKeyProvider(), TimeProvider.System),
            new AtprotoTenantOriginResolver(
                Options.Create(new AtprotoAuthenticationOptions { PublicUrl = "https://events.example.com/" }),
                Options.Create(new TenantConfiguration
                {
                    DefaultTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
                    DefaultTenant = "default"
                }),
                environment),
            new AtprotoAuthenticationMetrics());
    }

    private static AtprotoClientKeyProvider CreateKeyProvider()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(true);
        string ring = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC", crv = "P-256",
                    x = Encode(parameters.Q.X!), y = Encode(parameters.Q.Y!), d = Encode(parameters.D!),
                    kid = "logging-anchor", use = "sig", alg = "ES256", status = "active"
                }
            }
        });
        return new AtprotoClientKeyProvider(
            Options.Create(new AtprotoClientKeyOptions { OAuthClientPrivateJwks = ring }));
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TerminalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class DualLogCapture : IDisposable
    {
        private readonly CapturingMicrosoftProvider _microsoft;
        private readonly IsolatedSerilogProvider _serilogProvider;
        private readonly Serilog.ILogger _serilog;

        public DualLogCapture(string category)
        {
            _microsoft = new CapturingMicrosoftProvider(category);
            var sink = new CapturingSerilogSink(category);
            _serilog = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
            _serilogProvider = new IsolatedSerilogProvider(_serilog);
            Factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(_microsoft);
                builder.AddProvider(_serilogProvider);
            });
            SerilogEntries = sink.Entries;
        }

        public ILoggerFactory Factory { get; }
        public ConcurrentQueue<CapturedLog> MicrosoftEntries => _microsoft.Entries;
        public ConcurrentQueue<CapturedLog> SerilogEntries { get; }

        public void AddProviders(IServiceCollection services)
        {
            services.AddSingleton<ILoggerProvider>(_microsoft);
            services.AddSingleton<ILoggerProvider>(_serilogProvider);
        }

        public void Dispose()
        {
            Factory.Dispose();
            (_serilog as IDisposable)?.Dispose();
        }
    }

    private sealed class CapturingMicrosoftProvider(string category) : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            new CaptureLogger(categoryName == category, Entries, () => _scopes);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(
            bool enabled,
            ConcurrentQueue<CapturedLog> entries,
            Func<IExternalScopeProvider> scopes) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => scopes().Push(state);
            public bool IsEnabled(LogLevel logLevel) => enabled;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!enabled) return;
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key,
                        pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)
                    : new Dictionary<string, string>();
                var capturedScopes = new List<string>();
                scopes().ForEachScope(static (scope, target) => target.Add(
                    Convert.ToString(scope, CultureInfo.InvariantCulture) ?? string.Empty), capturedScopes);
                entries.Enqueue(new CapturedLog(
                    formatter(state, exception), properties, exception?.ToString(), capturedScopes));
            }
        }
    }

    private sealed class IsolatedSerilogProvider(Serilog.ILogger logger) : ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            new BridgeLogger(logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, categoryName));
        public void Dispose() { }

        private sealed class BridgeLogger(Serilog.ILogger logger) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Serilog.ILogger contextual = logger;
                if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                {
                    foreach (KeyValuePair<string, object?> property in properties.Where(p => p.Key != "{OriginalFormat}"))
                        contextual = contextual.ForContext(property.Key, property.Value, false);
                }
                contextual.Write(LogEventLevel.Debug, exception, "{RenderedMessage}", formatter(state, exception));
            }
        }
    }

    private sealed class CapturingSerilogSink(string category) : ILogEventSink
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public void Emit(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue(Serilog.Core.Constants.SourceContextPropertyName, out LogEventPropertyValue? source)
                || Format(source) != category) return;
            Entries.Enqueue(new CapturedLog(
                logEvent.RenderMessage(CultureInfo.InvariantCulture),
                logEvent.Properties.ToDictionary(pair => pair.Key, pair => Format(pair.Value)),
                logEvent.Exception?.ToString(), []));
        }
        private static string Format(LogEventPropertyValue value) => value switch
        {
            ScalarValue scalar => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString()
        };
    }

    private sealed record CapturedLog(
        string Message,
        IReadOnlyDictionary<string, string> Properties,
        string? Exception,
        IReadOnlyList<string> Scopes)
    {
        public string Flattened => string.Join('|',
            [Message, .. Properties.Select(pair => $"{pair.Key}={pair.Value}"), Exception ?? string.Empty, .. Scopes]);
    }
}
