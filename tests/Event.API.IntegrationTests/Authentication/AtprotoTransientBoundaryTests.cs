// ABOUTME: Exercises real HTTP pre-crypto admission limits, dependency failure mapping and machine-only logging.
// ABOUTME: Uses external secret authority faults and actual unavailable PostgreSQL relations, never mocked repositories.

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Explore.Application.Contracts.Secrets;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Explore.API.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientBoundaryTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RealDatabaseFailures_AreGenericUnavailable_AndDoNotRetryAssertions(bool replayUnavailable)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string disable = replayUnavailable
            ? "ALTER TABLE islamu_event.atproto_transient_assertion_replays RENAME TO transient_dependency_unavailable"
            : "ALTER TABLE islamu_event.atproto_transient_records RENAME TO transient_dependency_unavailable";
        string restore = replayUnavailable
            ? "ALTER TABLE islamu_event.transient_dependency_unavailable RENAME TO atproto_transient_assertion_replays"
            : "ALTER TABLE islamu_event.transient_dependency_unavailable RENAME TO atproto_transient_records";
        byte[] body = fixture.ReadBody();
        string assertion = fixture.Sign(body);
        await db.Database.ExecuteSqlRawAsync(disable);
        try
        {
            using var request = fixture.Request(body, assertion);
            using var response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            string output = await response.Content.ReadAsStringAsync();
            await Assert.That(output.Contains("transient_dependency_unavailable", StringComparison.Ordinal)).IsFalse();
            await Assert.That(output.Contains(assertion, StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(restore);
        }
        // A failed action already spent its assertion, unlike a replay admission whose INSERT never ran.
        if (!replayUnavailable)
        {
            using var retry = fixture.Request(body, assertion);
            using var response = await fixture.Client.SendAsync(retry);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        using var fresh = fixture.Request(body, fixture.Sign(body));
        using var recovered = await fixture.Client.SendAsync(fresh);
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task BodyAndHeaderBounds_PrecedeUnavailableKeyAuthority_ForKnownAndChunkedLengths()
    {
        var unavailable = Substitute.For<ISecretResolver>();
        unavailable.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Unavailable);
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISecretResolver>();
            services.AddSingleton(unavailable);
        }));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        byte[] body = fixture.ReadBody();
        string token = fixture.Sign(body);
        foreach (bool chunked in new[] { false, true })
        {
            using var oversized = fixture.Request(new byte[80 * 1024 + 1], token);
            if (chunked)
            {
                oversized.Content?.Dispose();
                oversized.Content = new ChunkedContent(new byte[80 * 1024 + 1]);
            }
            using var response = await client.SendAsync(oversized);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
        using var longToken = fixture.Request(body, new string('a', 4097));
        using var rejected = await client.SendAsync(longToken);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var altered = fixture.Request([.. body, (byte)' '], token);
        using var alteredResponse = await client.SendAsync(altered);
        await Assert.That(alteredResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var valid = fixture.Request(body, token);
        using var unavailableResponse = await client.SendAsync(valid);
        await Assert.That(unavailableResponse.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(unavailableResponse.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task EnabledEarlyRateLimit_RejectsBeforeKeyAuthority_WithoutTimingWaits()
    {
        var unavailable = Substitute.For<ISecretResolver>();
        unavailable.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Unavailable);
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:DisableInTesting"] = "false", ["RateLimiting:AtprotoTransient:PermitLimit"] = "1",
                ["RateLimiting:AtprotoTransient:WindowSeconds"] = "3600"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton(unavailable);
            });
        });
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        byte[] body = fixture.ReadBody();
        using var first = fixture.Request(body, fixture.Sign(body));
        using var admitted = await client.SendAsync(first);
        await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        using var slowBody = new SlowBody();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var throttled = await host.Server.SendAsync(context => ConfigureSlowRequest(context, fixture.Sign(body), slowBody), deadline.Token);
        await Assert.That(throttled.Response.StatusCode).IsEqualTo(StatusCodes.Status429TooManyRequests);
        await Assert.That(throttled.Response.Headers.CacheControl.ToString()).IsEqualTo("no-store");
        await Assert.That(throttled.Response.Headers["X-RateLimit-Limit"].ToString()).IsEqualTo("1");
        await Assert.That(slowBody.Started.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task SlowBody_IsCancelledByPrivateRequestTimeout_BeforeAuthentication()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.PostConfigure<RequestTimeoutOptions>(options =>
            {
                var policy = options.Policies[AtprotoTransientAuthenticationDefaults.Scheme];
                options.AddPolicy(AtprotoTransientAuthenticationDefaults.Scheme, new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(1),
                    TimeoutStatusCode = policy.TimeoutStatusCode,
                    WriteTimeoutResponse = policy.WriteTimeoutResponse
                });
            })));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var slowBody = new SlowBody();
        Task started = slowBody.Started.Task;
        Task cancelled = slowBody.Cancelled.Task;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IHttpRequestTimeoutFeature? timeoutFeature = null;
        Task<HttpContext> pending = host.Server.SendAsync(context =>
        {
            ConfigureSlowRequest(context, fixture.Sign(fixture.ReadBody()), slowBody);
            slowBody.ReadStarting = () => timeoutFeature = context.Features.Get<IHttpRequestTimeoutFeature>();
        }, deadline.Token);
        await started.WaitAsync(deadline.Token);
        await Assert.That(timeoutFeature).IsNotNull();
        await Assert.That(host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestTimeoutOptions>>()
            .Value.Policies[AtprotoTransientAuthenticationDefaults.Scheme].Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
        await cancelled.WaitAsync(deadline.Token);
        HttpContext completed = await pending.WaitAsync(deadline.Token);
        await Assert.That(completed.Response.StatusCode).IsEqualTo(StatusCodes.Status504GatewayTimeout);
        await Assert.That(completed.Response.Headers.CacheControl.ToString()).IsEqualTo("no-store");
        await Assert.That(completed.Response.ContentType).Contains("application/problem+json");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalRequestLogs_ContainNeitherBodyNorAssertion_AndNoFabricatedUserOrTenant(
        bool restartCompanionHost)
    {
        using var logs = new CapturingLogs();
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Capture this host's ILogger events independently of other test hosts' logging configuration.
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(logging =>
                logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs)));
        }));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        if (restartCompanionHost)
        {
            await using var companionHost = fixture.Factory.WithWebHostBuilder(_ => { });
            using var companionClient = companionHost.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        }
        byte[] body = fixture.ReadBody();
        string token = fixture.Sign(body);
        string correlationId = Guid.CreateVersion7().ToString("N");
        Task<IReadOnlyDictionary<string, object?>> logged = logs.ExpectRequest(
            correlationId, "POST", AtprotoTransientApiFixture.Prefix + "read");
        using var request = fixture.Request(body, token);
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.GetValues("X-Correlation-ID").Single()).IsEqualTo(correlationId);
        IReadOnlyDictionary<string, object?> requestLog = await logged.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(requestLog["PlatformIdentityPresent"] as bool?).IsFalse();
        await Assert.That(requestLog["IsAuthenticated"] as bool?).IsTrue();
        await Assert.That(requestLog["TenantPresent"] as bool?).IsFalse();
        foreach (string entry in logs.Entries)
        {
            await Assert.That(entry.Contains(token, StringComparison.Ordinal)).IsFalse();
            await Assert.That(entry.Contains(Encoding.UTF8.GetString(body), StringComparison.Ordinal)).IsFalse();
        }
    }

    private static void ConfigureSlowRequest(HttpContext context, string assertion, Stream body)
    {
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = AtprotoTransientApiFixture.Prefix + "read";
        context.Request.Headers[AtprotoTransientApiFixture.Header] = assertion;
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
    }

    private sealed class SlowBody : Stream
    {
        public Action? ReadStarting { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> data = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarting?.Invoke();
            Started.TrySetResult();
            try
            {
                return await data.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ChunkedContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        private RequestLogSubscription? subscription;
        public ConcurrentQueue<string> Entries { get; } = new();
        public Task<IReadOnlyDictionary<string, object?>> ExpectRequest(string correlationId, string method, string path)
        {
            var pending = new RequestLogSubscription(correlationId, method, path,
                new(TaskCreationOptions.RunContinuationsAsynchronously));
            subscription = pending;
            return pending.Completion.Task;
        }

        private sealed record RequestLogSubscription(string CorrelationId, string Method, string Path,
            TaskCompletionSource<IReadOnlyDictionary<string, object?>> Completion);
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }
        private sealed class Capture(CapturingLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                string text = formatter(state, exception) + exception;
                owner.Entries.Enqueue(text);
                if (category == "Explore.API.Middleware.RequestLoggingMiddleware"
                    && owner.subscription is { } pending
                    && state is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    var properties = fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
                    if (properties.TryGetValue("CorrelationId", out var correlationId)
                        && string.Equals(correlationId as string, pending.CorrelationId, StringComparison.Ordinal)
                        && properties.TryGetValue("Method", out var method)
                        && string.Equals(method as string, pending.Method, StringComparison.Ordinal)
                        && properties.TryGetValue("Path", out var path)
                        && string.Equals(path as string, pending.Path, StringComparison.Ordinal))
                        pending.Completion.TrySetResult(properties);
                }
            }
        }
    }
}
