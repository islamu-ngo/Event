// ABOUTME: Exercises BFF transient serialization and signing against the real API and PostgreSQL repositories.
// ABOUTME: Guards replica races, candidate ABA, tenant binding and indeterminate consumption without Redis.

extern alias bff;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BffAuth = bff::Explore.Blazor.Services.Auth;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientBffTransportTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    [Arguments("oauth_state")]
    [Arguments("tenant_handoff")]
    public async Task IndependentBffTransports_RequireTenantAndReturnExactlyOneConsumptionWinner(string purpose)
    {
        await using var first = await fixture.CreateBffServicesAsync();
        await using var second = await fixture.CreateBffServicesAsync();
        var writer = first.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        var reader = second.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        Guid other = await fixture.SeedTenantAsync();
        string token = NewToken();
        byte[] payload = RandomNumberGenerator.GetBytes(4096);
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(1);

        await Assert.That(await writer.CreateAsync(purpose, token, tenant, payload, expiry)).IsTrue();
        await Assert.That(await reader.CreateAsync(purpose, token, tenant, payload, expiry)).IsFalse();
        await Assert.That(await reader.ReadAsync(purpose, token, other)).IsNull();
        var candidate = await reader.ReadAsync(purpose, token, purpose == "oauth_state" ? null : tenant);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(candidate!.TenantId).IsEqualTo(tenant);
        await Assert.That(Convert.FromBase64String(candidate.ProtectedPayload)).IsEquivalentTo(payload);
        await Assert.That(await reader.ConsumeAsync(candidate with { TenantId = other })).IsFalse();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<bool> Consume(BffAuth.ApiBackedAtprotoTransientStore store)
        {
            await start.Task.WaitAsync(deadline.Token);
            return await store.ConsumeAsync(candidate, deadline.Token);
        }
        Task<bool>[] contenders = [Consume(writer), Consume(reader)];
        start.SetResult();
        bool[] results = await Task.WhenAll(contenders);
        await Assert.That(results.Count(won => won)).IsEqualTo(1);
        await Assert.That(await writer.ReadAsync(purpose, token, tenant)).IsNull();
    }

    [Test]
    public async Task RecreatedLocator_CannotBeConsumedUsingAnOldCandidate()
    {
        await using var services = await fixture.CreateBffServicesAsync();
        var store = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        byte[] payload = RandomNumberGenerator.GetBytes(64);
        await Assert.That(await store.CreateAsync("oauth_state", token, tenant, payload, expiry)).IsTrue();
        var old = await store.ReadAsync("oauth_state", token);
        await Assert.That(old).IsNotNull();
        await Assert.That(await store.ConsumeAsync(old!)).IsTrue();
        await Assert.That(await store.CreateAsync("oauth_state", token, tenant, payload, expiry)).IsTrue();
        await Assert.That(await store.ConsumeAsync(old!)).IsFalse();
        var current = await store.ReadAsync("oauth_state", token);
        await Assert.That(current).IsNotNull();
        await Assert.That(current!.Id).IsNotEqualTo(old!.Id);
        await Assert.That(await store.ConsumeAsync(current)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostCommittedConsumeResponse_IsNotRetriedByTheRegisteredPipeline(bool globalHedging)
    {
        using var loss = new LoseConsumeResponseHandler { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var services = await fixture.CreateBffServicesAsync(loss, globalHedging: globalHedging);
        var store = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await store.CreateAsync("oauth_state", token, tenant,
            RandomNumberGenerator.GetBytes(64), fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();
        var candidate = await store.ReadAsync("oauth_state", token);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(async () => await store.ConsumeAsync(candidate!)).Throws<HttpRequestException>();
        await Assert.That(loss.ConsumeAttempts).IsEqualTo(1);
        await Assert.That(await store.ReadAsync("oauth_state", token)).IsNull();
    }

    [Test]
    public async Task RotatedActiveTransportKey_RemainsAcceptedDuringApiKeyOverlap()
    {
        await using var original = await fixture.CreateBffServicesAsync();
        await using var rotated = await fixture.CreateBffServicesAsync(rotateKeys: true);
        var writer = original.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        var reader = rotated.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await writer.CreateAsync("oauth_state", token, tenant,
            RandomNumberGenerator.GetBytes(64), fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();
        await Assert.That(rotated.GetRequiredService<BffAuth.AtprotoClientKeyProvider>().ActiveKeyId).IsEqualTo("transient-retiring");
        var candidate = await reader.ReadAsync("oauth_state", token);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(await reader.ConsumeAsync(candidate!)).IsTrue();
    }

    [Test]
    public async Task BodyChangedAfterSigning_CannotCreateARecord()
    {
        using var tamper = new TamperCreateBodyHandler { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var services = await fixture.CreateBffServicesAsync(tamper);
        var store = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(async () => await store.CreateAsync("oauth_state", token, tenant,
            RandomNumberGenerator.GetBytes(64), fixture.Clock.GetUtcNow().AddMinutes(1))).Throws<InvalidOperationException>();
        await Assert.That(await store.ReadAsync("oauth_state", token)).IsNull();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("purpose")]
    [Arguments("digest")]
    [Arguments("expired")]
    [Arguments("oversize")]
    public async Task UnusableReadResponses_NeverReachTheConsumerOrDestroyStoredState(string corruption)
    {
        using var corrupt = new CorruptReadResponseHandler(corruption) { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var damaged = await fixture.CreateBffServicesAsync(corrupt);
        await using var healthy = await fixture.CreateBffServicesAsync();
        var damagedStore = damaged.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        var healthyStore = healthy.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await healthyStore.CreateAsync("oauth_state", token, tenant,
            RandomNumberGenerator.GetBytes(64), fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();
        if (corruption == "oversize")
            await Assert.That(async () => await damagedStore.ReadAsync("oauth_state", token, tenant)).Throws<HttpRequestException>();
        else
            await Assert.That(async () => await damagedStore.ReadAsync("oauth_state", token, tenant)).Throws<InvalidOperationException>();
        var candidate = await healthyStore.ReadAsync("oauth_state", token, tenant);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(await healthyStore.ConsumeAsync(candidate!)).IsTrue();
    }

    [Test]
    public async Task MalformedResponse_DoesNotLeakPayloadOrAssertionThroughExceptionsAndTraceLogs()
    {
        string marker = NewToken();
        byte[] payload = RandomNumberGenerator.GetBytes(64);
        using var fault = new MalformedReadResponseHandler(marker) { InnerHandler = fixture.Factory.Server.CreateHandler() };
        using var logs = new CapturedLogs();
        await using var services = await fixture.CreateBffServicesAsync(fault, logs: logs);
        var store = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await store.CreateAsync("oauth_state", token, tenant, payload,
            fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();
        Exception? failure = null;
        try { await store.ReadAsync("oauth_state", token); }
        catch (InvalidOperationException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(logs.Messages.Any(message => message.Contains("HTTP", StringComparison.Ordinal))).IsTrue();
        await Assert.That(fault.Assertion).IsNotNull();
        string diagnostics = string.Join('\n', logs.Messages) + failure;
        foreach (string sensitive in new[] { marker, token, Convert.ToBase64String(payload), fault.Assertion! })
            await Assert.That(diagnostics.Contains(sensitive, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ChangedConsumeResult_CannotAuthorizeUseOfTheValidatedCandidate()
    {
        using var fault = new ChangedConsumeResponseHandler { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var services = await fixture.CreateBffServicesAsync(fault);
        var store = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await store.CreateAsync("oauth_state", token, tenant, RandomNumberGenerator.GetBytes(64),
            fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();
        var candidate = await store.ReadAsync("oauth_state", token);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(async () => await store.ConsumeAsync(candidate!)).Throws<InvalidOperationException>();
        await Assert.That(await store.ReadAsync("oauth_state", token)).IsNull();
    }

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StalledResponseBody_IsCancelledWithoutRetryOrDestroyingState(bool cancelCaller)
    {
        using var stall = new StallReadBodyHandler { InnerHandler = fixture.Factory.Server.CreateHandler() };
        await using var damaged = await fixture.CreateBffServicesAsync(stall);
        await using var healthy = await fixture.CreateBffServicesAsync();
        var writer = healthy.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        var reader = damaged.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        Guid tenant = await fixture.SeedTenantAsync();
        string token = NewToken();
        await Assert.That(await writer.CreateAsync("oauth_state", token, tenant,
            RandomNumberGenerator.GetBytes(64), fixture.Clock.GetUtcNow().AddMinutes(1))).IsTrue();

        using var caller = new CancellationTokenSource();
        Task<BffAuth.BffAtprotoTransientCandidate?> read = reader.ReadAsync("oauth_state", token, cancellationToken: caller.Token);
        try
        {
            await stall.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (cancelCaller) await caller.CancelAsync();
            // The transport's own twenty-second deadline must cover content after headers have arrived.
            await Assert.That(async () => await read.WaitAsync(TimeSpan.FromSeconds(30)))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            stall.Release.TrySetResult();
            try { await read; }
            catch (OperationCanceledException) { }
        }
        await Assert.That(stall.ReadAttempts).IsEqualTo(1);
        var candidate = await writer.ReadAsync("oauth_state", token);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(await writer.ConsumeAsync(candidate!)).IsTrue();
    }

    private sealed class StallReadBodyHandler : DelegatingHandler
    {
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadAttempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/read", StringComparison.Ordinal)
                && response.StatusCode == HttpStatusCode.OK)
            {
                ReadAttempts++;
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                response.Content.Dispose();
                response.Content = new StalledContent(body, BodyStarted, Release);
                response.Content.Headers.ContentType = new("application/json");
            }
            return response;
        }

        private sealed class StalledContent(byte[] body, TaskCompletionSource started, TaskCompletionSource release) : HttpContent
        {
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                SerializeToStreamAsync(stream, context, CancellationToken.None);
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
            }
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception);
        }
    }

    private sealed class MalformedReadResponseHandler(string marker) : DelegatingHandler
    {
        public string? Assertion { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/read", StringComparison.Ordinal) && response.StatusCode == HttpStatusCode.OK)
            {
                Assertion = request.Headers.GetValues(BffAuth.AtprotoTransientAssertionService.HeaderName).Single();
                response.Content.Dispose();
                response.Content = new StringContent("{\"protectedPayload\":\"" + marker + "\", invalid}");
                response.Content.Headers.ContentType = new("application/json");
            }
            return response;
        }
    }

    private sealed class ChangedConsumeResponseHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/consume", StringComparison.Ordinal) && response.StatusCode == HttpStatusCode.OK)
            {
                var content = response.Content;
                var document = JsonNode.Parse(await content.ReadAsStringAsync(cancellationToken))!;
                document["id"] = Guid.CreateVersion7();
                response.Content = JsonContent.Create(document);
                content.Dispose();
            }
            return response;
        }
    }

    private sealed class TamperCreateBodyHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/create", StringComparison.Ordinal))
            {
                var content = request.Content!;
                byte[] body = await content.ReadAsByteArrayAsync(cancellationToken);
                request.Content = new ByteArrayContent([.. body, (byte)' ']);
                request.Content.Headers.ContentType = new("application/json");
                content.Dispose();
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CorruptReadResponseHandler(string corruption) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/read", StringComparison.Ordinal) && response.StatusCode == HttpStatusCode.OK)
            {
                var content = response.Content;
                var document = JsonNode.Parse(await content.ReadAsStringAsync(cancellationToken))!;
                switch (corruption)
                {
                    case "tenant": document["tenantId"] = Guid.CreateVersion7(); break;
                    case "purpose": document["purpose"] = "tenant_handoff"; break;
                    case "digest": document["tokenDigest"] = NewToken(); break;
                    case "expired": document["expiresAtUnixMilliseconds"] = 0; break;
                    case "oversize": document["protectedPayload"] = new string('a', 81 * 1024); break;
                }
                response.Content = JsonContent.Create(document);
                content.Dispose();
            }
            return response;
        }
    }

    private sealed class LoseConsumeResponseHandler : DelegatingHandler
    {
        public int ConsumeAttempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool consume = request.RequestUri!.AbsolutePath.EndsWith("/consume", StringComparison.Ordinal);
            if (consume) ConsumeAttempts++;
            var response = await base.SendAsync(request, cancellationToken);
            if (consume && response.StatusCode == HttpStatusCode.OK)
            {
                response.Dispose();
                throw new HttpRequestException("Simulated response loss after durable consumption.");
            }
            return response;
        }
    }
}
