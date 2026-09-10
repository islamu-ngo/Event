using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Explore.Blazor.IntegrationTests.Endpoints;

// The real split host owns process-global Serilog state, including during disposal.
[NotInParallel]
public sealed class BffRequestLoggingBoundaryTests
{
    private const string PathPrefix = "/api/v1/logging-boundary/";
    private const string Query = "?boundary-query=unlogged-query-sentinel";
    private static readonly string ApplicationCategory = typeof(Program).Assembly.GetName().Name!;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(20);
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task MethodControlOctets_AreRejectedBeforeApplicationAndDiagnosticLogging()
    {
        var observation = new RequestObservation();
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, observation);
        using var client = CreateClient(factory);
        string path = PathPrefix + "method-control";

        // A valid request over the same raw transport proves that the protected route
        // and both real logging surfaces are live before absence assertions begin.
        Task<CompletedRequest> before = observation.NextCompletionAsync();
        await Assert.That(await SendRawRequestAsync(client.BaseAddress!, "GET", path))
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertRequestAsync(await before, "GET", path, path);
        await AssertDiagnosticLogsAsync(observation, 1, "GET", path);
        int entered = observation.EnteredRequests;

        // RFC 9110 token grammar excludes every ASCII control octet, not only CR/LF.
        // HttpClient cannot send these methods: the bytes must reach Kestrel itself.
        foreach (int octet in Enumerable.Range(0, 32).Append(127))
        {
            HttpStatusCode status = await SendRawRequestAsync(
                client.BaseAddress!, "G" + (char)octet + "ET", path);
            await Assert.That(status).IsEqualTo(HttpStatusCode.BadRequest)
                .Because($"method octet 0x{octet:X2} must be rejected at the HTTP parser");
            // SendRawRequestAsync consumes EOF, so the rejected connection cannot
            // subsequently enter application middleware or emit the target event.
            await Assert.That(observation.EnteredRequests).IsEqualTo(entered);
            await Assert.That(observation.Logs.MicrosoftEntries.Count).IsEqualTo(1);
            await Assert.That(observation.Logs.SerilogEntries.Count).IsEqualTo(1);
        }

        Task<CompletedRequest> after = observation.NextCompletionAsync();
        await Assert.That(await SendRawRequestAsync(client.BaseAddress!, "GET", path))
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertRequestAsync(await after, "GET", path, path);
        await AssertDiagnosticLogsAsync(observation, 2, "GET", path);
        await Assert.That(observation.EnteredRequests).IsEqualTo(entered + 1);
    }

    [Test]
    [Arguments("%0D", "\r")]
    [Arguments("%0A", "\n")]
    [Arguments("%0D%0A", "\r\n")]
    [Arguments("%1B", "\u001b")]
    [Arguments("%09", "\t")]
    [Arguments("%7F", "\u007f")]
    [Arguments("", "")]
    public async Task EncodedControlPaths_StayEncodedInFormattedAndStructuredDiagnostics(
        string encodedControl, string decodedControl)
    {
        var observation = new RequestObservation();
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, observation);
        using var client = CreateClient(factory);
        string encodedPath = PathPrefix + "path-sentinel" + encodedControl + "tail";
        string decodedPath = PathPrefix + "path-sentinel" + decodedControl + "tail";

        // The authenticated response proves the controller actually executes for
        // this very path; a 404, redirect, or parser rejection cannot make this pass.
        using var authenticated = new HttpRequestMessage(HttpMethod.Get, encodedPath + Query);
        authenticated.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000002")));
        Task<CompletedRequest> authenticatedCompletion = observation.NextCompletionAsync();
        using var authenticatedResponse = await client.SendAsync(authenticated, Token);
        CompletedRequest authenticatedRequest = await authenticatedCompletion;
        await Assert.That(authenticatedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await authenticatedResponse.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        await Assert.That(body.RootElement.GetProperty("method").GetString()).IsEqualTo("GET");
        await Assert.That(body.RootElement.GetProperty("path").GetString()).IsEqualTo(decodedPath);
        await Assert.That(body.RootElement.GetProperty("authenticated").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.GetProperty("requiresAuthorization").GetBoolean()).IsTrue();
        await AssertRequestAsync(authenticatedRequest, "GET", decodedPath, encodedPath + Query);
        await Assert.That(observation.Logs.MicrosoftEntries).IsEmpty();
        await Assert.That(observation.Logs.SerilogEntries).IsEmpty();

        Task<CompletedRequest> anonymousCompletion = observation.NextCompletionAsync();
        using var anonymousResponse = await client.GetAsync(encodedPath + Query, Token);
        CompletedRequest anonymousRequest = await anonymousCompletion;
        await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertRequestAsync(anonymousRequest, "GET", decodedPath, encodedPath + Query);
        await AssertDiagnosticLogsAsync(observation, 1, "GET", encodedPath);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        BlazorBffWebApplicationFactory root, RequestObservation observation)
    {
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Restore only the concrete service removed by the shared fixture.
            // Keep its auth handler and the native tenant/session/CSRF pipeline.
            services.AddScoped<BffAdminClaimsTransformation>();
            services.AddSingleton<ILoggerProvider>(observation.Logs);
            // Production ReadFrom.Services discovers this sink; no replacement logger,
            // parser, middleware, or reconstructed Serilog bridge is installed.
            services.AddSingleton<ILogEventSink>(observation.Logs);
            services.AddSingleton<IStartupFilter>(observation);
        }));
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var addresses = factory.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            BaseAddress = new Uri(addresses.Addresses.Single()),
            Timeout = CompletionTimeout
        };
    }

    private static async Task<HttpStatusCode> SendRawRequestAsync(Uri address, string method, string path)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(CompletionTimeout);
        using var connection = new TcpClient(AddressFamily.InterNetwork);
        await connection.ConnectAsync(IPAddress.Loopback, address.Port, timeout.Token);
        await using NetworkStream stream = connection.GetStream();
        byte[] request = Encoding.ASCII.GetBytes(
            method + " " + path + " HTTP/1.1\r\nHost: " + address.Authority
            + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, timeout.Token);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? statusLine = await reader.ReadLineAsync(timeout.Token);
        await reader.ReadToEndAsync(timeout.Token);
        await Assert.That(statusLine).IsNotNull();
        string[] parts = statusLine!.Split(' ', 3);
        await Assert.That(parts[0]).IsEqualTo("HTTP/1.1");
        return (HttpStatusCode)int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static async Task AssertRequestAsync(
        CompletedRequest request, string method, string decodedPath, string rawTarget)
    {
        await Assert.That(request.Method).IsEqualTo(method);
        await Assert.That(request.DecodedPath).IsEqualTo(decodedPath);
        await Assert.That(request.RawTarget).IsEqualTo(rawTarget);
    }

    private static async Task AssertDiagnosticLogsAsync(
        RequestObservation observation, int expectedCount, string method, string encodedPath)
    {
        foreach (CapturedLog[] logs in new[]
        {
            observation.Logs.MicrosoftEntries.ToArray(),
            observation.Logs.SerilogEntries.ToArray()
        })
        {
            await Assert.That(logs.Length).IsEqualTo(expectedCount);
            foreach (CapturedLog log in logs)
            {
                await Assert.That(log.Fields["Method"]).IsEqualTo(method);
                await Assert.That(log.Fields["Path"]).IsEqualTo(encodedPath);
                await Assert.That(log.Message).Contains(encodedPath);
                foreach (string value in log.Fields.Values.Append(log.Message))
                {
                    await Assert.That(value.Any(char.IsControl)).IsFalse();
                    await Assert.That(value).DoesNotContain("unlogged-query-sentinel");
                }
            }
        }
    }

    private sealed record CompletedRequest(string Method, string DecodedPath, string RawTarget);
    private sealed record CapturedLog(string Message, IReadOnlyDictionary<string, string> Fields);

    private sealed class RequestObservation : IStartupFilter
    {
        private readonly Channel<CompletedRequest> _completed = Channel.CreateUnbounded<CompletedRequest>();
        private int _enteredRequests;
        public int EnteredRequests => Volatile.Read(ref _enteredRequests);
        public BoundaryLogs Logs { get; } = new();

        public Task<CompletedRequest> NextCompletionAsync() =>
            _completed.Reader.ReadAsync(Token).AsTask().WaitAsync(CompletionTimeout, Token);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                Interlocked.Increment(ref _enteredRequests);
                if (context.Request.Path.StartsWithSegments("/api/v1/logging-boundary"))
                {
                    // Observe Kestrel's parsed values, never assign Method or Path.
                    var request = new CompletedRequest(
                        context.Request.Method,
                        context.Request.Path.Value!,
                        context.Features.Get<IHttpRequestFeature>()!.RawTarget);
                    context.Response.OnCompleted(() => _completed.Writer.WriteAsync(request).AsTask());
                }
                await continuation(context);
            });
            next(app);
        };
    }

    private sealed class BoundaryLogs : ILoggerProvider, ILogEventSink
    {
        public ConcurrentQueue<CapturedLog> MicrosoftEntries { get; } = new();
        public ConcurrentQueue<CapturedLog> SerilogEntries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new BoundaryLogger(categoryName, MicrosoftEntries);
        public void Dispose() { }

        public void Emit(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue(Serilog.Core.Constants.SourceContextPropertyName, out var source)
                || source is not ScalarValue { Value: string category }
                || category != ApplicationCategory
                || !logEvent.Properties.ContainsKey("Method")
                || !logEvent.Properties.ContainsKey("Path")) return;

            SerilogEntries.Enqueue(new CapturedLog(
                logEvent.RenderMessage(CultureInfo.InvariantCulture),
                logEvent.Properties.ToDictionary(pair => pair.Key, pair => pair.Value is ScalarValue scalar
                    ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty
                    : pair.Value.ToString())));
        }

        private sealed class BoundaryLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category == ApplicationCategory;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel) || state is not IEnumerable<KeyValuePair<string, object?>> values) return;
                var fields = values.ToDictionary(pair => pair.Key,
                    pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                // Match machine-consumed structured fields, not diagnostic prose.
                if (!fields.ContainsKey("Method") || !fields.ContainsKey("Path")) return;
                entries.Enqueue(new CapturedLog(formatter(state, exception), fields));
            }
        }
    }
}
