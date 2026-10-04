using System.Net;
using System.Net.Http.Headers;

namespace ISLAMU.DependencySubmission.Tests;

public sealed class SubmissionTests
{
    private static DependencySnapshot Snapshot => SnapshotBuilder.Build(SnapshotTests.Context,
        SnapshotBuilder.BuildManifests([new LockInput("src/App/packages.lock.json",
            """{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Direct","resolved":"1.0.0"}}}}""")]));
    private static Uri Endpoint => new("https://api.github.com/repos/islamu-ngo/Event/dependency-graph/snapshots");

    [Test]
    public async Task Submit_Accepted_UsesOfficialInterfaceAndSafeLogWithoutReadingBody()
    {
        var logs = new List<SubmissionLog>();
        using var handler = new DelegateHandler(async (request, _) =>
        {
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(request.RequestUri).IsEqualTo(Endpoint);
            await Assert.That(request.Headers.GetValues("X-GitHub-Api-Version").Single()).IsEqualTo("2026-03-10");
            await Assert.That(request.Content!.Headers.ContentType!.MediaType).IsEqualTo("application/json");
            await Assert.That(request.Headers.Accept.Single().MediaType).IsEqualTo("application/vnd.github+json");
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new UnreadableContent() };
            response.Headers.TryAddWithoutValidation("X-GitHub-Request-Id", "ABC:123-DEF");
            return response;
        });
        using var http = new HttpClient(handler);
        var result = await new SubmissionClient(http, new ManualTimeProvider(), logs.Add).SubmitAsync(Endpoint, Snapshot);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        SubmissionLogging.Write(writer, logs.Single());
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.Success);
        await Assert.That(logs.Single().RequestId).IsEqualTo("ABC:123-DEF");
        await Assert.That(writer.ToString()).IsEqualTo(
            $"status=201 attempt=1 classification=accepted delay_ms=0 request_id=ABC:123-DEF result=success{Environment.NewLine}");
    }

    [Test]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.BadGateway)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.GatewayTimeout)]
    public async Task Submit_TransientStatus_ThreeTotalAttemptsWithIdenticalBody(HttpStatusCode status)
    {
        var time = new ManualTimeProvider();
        var logs = new List<SubmissionLog>();
        var bodies = new List<string>();
        using var handler = new DelegateHandler(async (request, cancellation) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellation));
            return new HttpResponseMessage(status);
        });
        using var http = new HttpClient(handler);
        var client = new SubmissionClient(http, time, logs.Add);
        var firstDelay = time.NextDelay();
        var pending = client.SubmitAsync(Endpoint, Snapshot);
        await firstDelay.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(bodies.Count).IsEqualTo(1);
        var secondDelay = time.NextDelay();
        time.Advance(TimeSpan.FromSeconds(1));
        await secondDelay.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(bodies.Count).IsEqualTo(2);
        time.Advance(TimeSpan.FromSeconds(2));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.AttemptsExhausted);
        await Assert.That(result.Attempts).IsEqualTo(3);
        await Assert.That(bodies.Distinct().Count()).IsEqualTo(1);
        await Assert.That(logs[^1].Classification).IsEqualTo(SubmissionClassification.ServerTransient);
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.UnprocessableEntity)]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.NotImplemented)]
    [Arguments(HttpStatusCode.Found)]
    public async Task Submit_OrdinaryErrors_TerminalWithoutRetry(HttpStatusCode status)
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(status) { Content = new StringContent("body must never be logged") };
            response.Headers.TryAddWithoutValidation("X-GitHub-Request-Id", "unsafe\nrequest");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var logs = new List<SubmissionLog>();
        var result = await new SubmissionClient(http, new ManualTimeProvider(), logs.Add).SubmitAsync(Endpoint, Snapshot);
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.Terminal);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(logs.Single().RequestId).IsNull();
        await Assert.That(logs.Single().Delay).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, false)]
    [Arguments(HttpStatusCode.TooManyRequests, false)]
    [Arguments(HttpStatusCode.Forbidden, true)]
    public async Task Submit_RateLimitEvidence_DoesNotRetryBeforeServerDelay(HttpStatusCode status, bool dateHeader)
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            var response = new HttpResponseMessage(++calls == 1 ? status : HttpStatusCode.Created);
            response.Headers.RetryAfter = dateHeader
                ? new RetryConditionHeaderValue(time.GetUtcNow().AddSeconds(20))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
            response.Headers.TryAddWithoutValidation("X-GitHub-Request-Id", "ABC:123-DEF");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        var delay = await registered.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(20));
        time.Advance(TimeSpan.FromSeconds(19));
        await Assert.That(calls).IsEqualTo(1);
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.Success);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Submit_PrimaryReset_UsesLaterOfResetAndRetryAfter()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            var response = new HttpResponseMessage(++calls == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", time.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        await Assert.That(await registered.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(TimeSpan.FromSeconds(31));
        time.Advance(TimeSpan.FromSeconds(31));
        await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome).IsEqualTo(SubmissionOutcome.Success);
    }

    [Test]
    public async Task Submit_ServerDelayBeyondBudget_StopsWithoutPrematureRetry()
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var logs = new List<SubmissionLog>();
        var result = await new SubmissionClient(http, new ManualTimeProvider(), logs.Add).SubmitAsync(Endpoint, Snapshot);
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.DeadlineExceeded);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(logs.Single().Delay).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Submit_429WithoutHints_WaitsOneMinute()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Created)));
        using var http = new HttpClient(handler);
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        await Assert.That(await registered.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));
        await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome).IsEqualTo(SubmissionOutcome.Success);
    }

    [Test]
    [Arguments("malformed", null)]
    [Arguments("-1", null)]
    [Arguments(null, "not-a-reset")]
    [Arguments(null, "9223372036854775807")]
    public async Task Submit_InvalidRateLimitHints_TerminalRatherThanGuessing(string? retryAfter, string? reset)
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            if (reset is not null)
            {
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset);
            }
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var logs = new List<SubmissionLog>();
        var result = await new SubmissionClient(http, new ManualTimeProvider(), logs.Add).SubmitAsync(Endpoint, Snapshot);
        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.Terminal);
        await Assert.That(logs.Single().Classification).IsEqualTo(SubmissionClassification.InvalidRetryHint);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Submit_OverallDeadline_CancelsRetryDelayWithoutAnotherRequest()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        await registered.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(SubmissionClient.OverallTimeout);
        await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome).IsEqualTo(SubmissionOutcome.DeadlineExceeded);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Submit_UntrustedEndpoint_RejectsBeforeSending()
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        using var http = new HttpClient(handler);
        await Assert.That(async () =>
            {
                await new SubmissionClient(http, new ManualTimeProvider(), _ => { })
                    .SubmitAsync(new Uri("https://example.org/repos/islamu-ngo/Event/dependency-graph/snapshots"), Snapshot);
            })
            .Throws<ArgumentException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(HttpRequestError.ConnectionError, true)]
    [Arguments(HttpRequestError.NameResolutionError, true)]
    [Arguments(HttpRequestError.ResponseEnded, true)]
    [Arguments(HttpRequestError.SecureConnectionError, false)]
    [Arguments(HttpRequestError.UserAuthenticationError, false)]
    [Arguments(HttpRequestError.Unknown, false)]
    public async Task Submit_TransportError_ClassifiesWithoutExceptionLogging(HttpRequestError error, bool retry)
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            if (++calls == 1)
                throw new HttpRequestException(error, "do not log exception details");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        using var http = new HttpClient(handler);
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        if (retry)
        {
            await registered.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(1));
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.Outcome).IsEqualTo(retry ? SubmissionOutcome.Success : SubmissionOutcome.Terminal);
        await Assert.That(calls).IsEqualTo(retry ? 2 : 1);
    }

    [Test]
    public async Task Submit_AttemptTimeout_RetriesThroughClockCancellation()
    {
        var time = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new DelegateHandler(async (_, cancellation) =>
        {
            if (++calls == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            }
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var registered = time.NextDelay();
        var pending = new SubmissionClient(http, time, _ => { }).SubmitAsync(Endpoint, Snapshot);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(SubmissionClient.AttemptTimeout);
        await registered.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome).IsEqualTo(SubmissionOutcome.Success);
    }

    [Test]
    public async Task Submit_CallerCancellation_NeverRetries()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var handler = new DelegateHandler(async (_, token) =>
        {
            calls++;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var http = new HttpClient(handler);
        var pending = new SubmissionClient(http, new ManualTimeProvider(), _ => { }).SubmitAsync(Endpoint, Snapshot, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome).IsEqualTo(SubmissionOutcome.Cancelled);
        await Assert.That(calls).IsEqualTo(1);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Response body must not be read.");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
