using System.Collections.Concurrent;
using System.Globalization;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Endpoints;

// Split-host composition owns process-global Serilog state during its lifetime.
[NotInParallel]
public sealed class BffAuthLoggingBoundaryTests
{
    private const string InputSentinel = "bff-untrusted-input-sentinel";
    private const string QuerySentinel = "bff-query-secret-sentinel";
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(20);
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments("\r")]
    [Arguments("\n")]
    [Arguments("\u001b")]
    [Arguments("")]
    public async Task Challenge_DoesNotLogUntrustedProviderOrQuery(string control)
    {
        string provider = "unknown-" + InputSentinel + control;
        var observation = new RequestObservation("/auth/challenge", "provider");
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, observation);
        using var client = CreateClient(factory);

        // The completion subscription exists before the request, including when logs
        // are emitted synchronously. No dependency on log prose or on a vulnerable field remaining.
        Task<CompletedRequest> completed = observation.Completed;
        using var response = await client.GetAsync(
            "/auth/challenge?provider=" + Uri.EscapeDataString(provider)
            + "&returnUrl=%2Fdashboard&querySecret=" + QuerySentinel, Token);
        CompletedRequest request = await completed.WaitAsync(CompletionTimeout, Token);

        await Assert.That(request.DecodedInput).IsEqualTo(provider);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.OriginalString)
            .IsEqualTo("/login?returnUrl=%2Fdashboard");
        await AssertPrivateLogsAsync(request.Logs);
    }

    [Test]
    [Arguments("\r")]
    [Arguments("\n")]
    [Arguments("\u001b")]
    [Arguments("")]
    public async Task Signout_DoesNotLogUntrustedReturnUrlOrQuery(string control)
    {
        string returnUrl = "/signed-out?token=" + InputSentinel + control;
        var observation = new RequestObservation("/auth/signout", "returnUrl");
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, observation);
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"), "Logging Boundary Tester"));

        Task<CompletedRequest> completed = observation.Completed;
        using var response = await client.GetAsync(
            "/auth/signout?returnUrl=" + Uri.EscapeDataString(returnUrl)
            + "&querySecret=" + QuerySentinel, Token);
        CompletedRequest request = await completed.WaitAsync(CompletionTimeout, Token);

        await Assert.That(request.DecodedInput).IsEqualTo(returnUrl);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.OriginalString)
            .IsEqualTo(control.Length == 0 ? returnUrl : "/");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        string cookieName = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name!;
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(cookieName + "=;", StringComparison.Ordinal)))
            .IsTrue();
        await AssertPrivateLogsAsync(request.Logs);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        BlazorBffWebApplicationFactory root, RequestObservation observation)
    {
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Keep the existing authentication, antiforgery, provider readiness and
            // return-URL services. Production Serilog forwards to native ILogger providers.
            services.AddScoped<BffAdminClaimsTransformation>();
            services.AddSingleton<ILoggerProvider>(observation.Logs);
            services.AddSingleton<IStartupFilter>(observation);
        }));
        // An explicit listener prevents application URL defaults from selecting a fixed test port.
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var addresses = factory.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true
        })
        {
            BaseAddress = new Uri(addresses.Addresses.Single()),
            Timeout = CompletionTimeout
        };
    }

    private static async Task AssertPrivateLogsAsync(IReadOnlyList<CapturedLog> logs)
    {
        await Assert.That(logs.Count).IsGreaterThan(0);
        var violations = new List<string>();
        foreach (CapturedLog log in logs)
        {
            Check("formatted output", log.Message);
            foreach (var field in log.Fields)
            {
                Check("structured field " + field.Key, field.Value);
            }
        }

        // Aggregate both output surfaces so a RED run identifies all leaks rather
        // than stopping at the first character. Only sentinel/control values are asserted.
        await Assert.That(violations).IsEmpty();

        void Check(string surface, string value)
        {
            foreach (var (forbidden, label) in new[]
            {
                ("\r", "decoded CR"), ("\n", "decoded LF"), ("\u001b", "decoded ESC"),
                (InputSentinel, "input sentinel"), (QuerySentinel, "query secret sentinel")
            })
            {
                if (value.Contains(forbidden, StringComparison.Ordinal))
                {
                    violations.Add(surface + " contains " + label);
                }
            }
        }
    }

    private sealed record CapturedLog(string Message, IReadOnlyDictionary<string, string> Fields);
    private sealed record CompletedRequest(string DecodedInput, IReadOnlyList<CapturedLog> Logs);

    private sealed class RequestObservation(string path, string queryKey) : IStartupFilter
    {
        private readonly TaskCompletionSource<CompletedRequest> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BoundaryLogProvider Logs { get; } = new();
        public Task<CompletedRequest> Completed => _completed.Task;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path == path)
                {
                    string decoded = context.Request.Query[queryKey].ToString();
                    context.Response.OnCompleted(() =>
                    {
                        _completed.TrySetResult(new CompletedRequest(decoded, Logs.Entries.ToArray()));
                        return Task.CompletedTask;
                    });
                }
                await continuation(context);
            });
            next(app);
        };
    }

    private sealed class BoundaryLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new BoundaryLogger(categoryName, Entries);
        public void Dispose() { }

        private sealed class BoundaryLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category == "AuthEndpoints";

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key,
                        pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)
                    : new Dictionary<string, string>();
                entries.Enqueue(new CapturedLog(formatter(state, exception), fields));
            }
        }
    }
}
