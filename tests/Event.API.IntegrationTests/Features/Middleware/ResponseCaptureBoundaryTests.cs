using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Middleware;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IO;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class ResponseCaptureBoundaryTests
{
    private const int ETagLimit = 256 * 1024;
    private const int ReceiptLimit = 1024 * 1024;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SmallResponsesKeepCaptureCapacitySmallAndRemainUsable(bool receipts, bool synchronous)
    {
        int capacity = -1;
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            if (synchronous)
                context.Response.Body.Write("{}"u8);
            else
                await context.Response.Body.WriteAsync("{}"u8.ToArray());

            // Inspect the captured memory without widening the internal stream's visibility.
            var captured = (ReadOnlyMemory<byte>)context.Response.Body.GetType()
                .GetProperty("CapturedBody")!.GetValue(context.Response.Body)!;
            await Assert.That(MemoryMarshal.TryGetArray(captured, out ArraySegment<byte> buffer)).IsTrue();
            capacity = buffer.Array!.Length;
            await Assert.That(captured.Length).IsEqualTo(2);
        }, receipts: receipts);

        using HttpResponseMessage response = await (receipts
            ? host.SendWriteAsync("small-body-key") : host.Client.GetAsync("/capture")).WaitAsync(Deadline);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(capacity >= 2 && capacity <= 4096).IsTrue();
        if (receipts)
        {
            using HttpResponseMessage replay = await host.SendWriteAsync("small-body-key").WaitAsync(Deadline);
            await Assert.That(replay.Headers.GetValues("X-Idempotency-Replay").Single()).IsEqualTo("true");
            await Assert.That(await replay.Content.ReadAsStringAsync()).IsEqualTo("{}");
        }
        else
        {
            await Assert.That(response.Headers.ETag).IsNotNull();
        }
    }

    [Test]
    [Arguments(ETagLimit - 1, false)]
    [Arguments(ETagLimit, false)]
    [Arguments(ETagLimit + 1, false)]
    [Arguments(ETagLimit + 1, true)]
    public async Task ETagThresholdPreservesEveryByteAndNeverRetainsBeyondCap(int size, bool knownLength)
    {
        byte[] body = Body(size);
        long retained = -1;
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Response-Marker"] = "preserved";
            if (knownLength) context.Response.ContentLength = body.Length;
            int prefix = Math.Min(ETagLimit, body.Length);
            await context.Response.Body.WriteAsync(body.AsMemory(0, prefix));
            await context.Response.Body.WriteAsync(body.AsMemory(prefix));
            retained = context.Response.Body.Length;
        });
        using HttpResponseMessage response = await host.Client.GetAsync("/capture").WaitAsync(Deadline);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
        await Assert.That(response.Headers.GetValues("X-Response-Marker").Single()).IsEqualTo("preserved");
        await Assert.That(response.Headers.ETag is not null).IsEqualTo(size <= ETagLimit);
        await Assert.That(retained <= ETagLimit).IsTrue();
        if (size > ETagLimit) await Assert.That(retained).IsEqualTo(0);
    }

    [Test]
    [Arguments("text/event-stream", true, false)]
    [Arguments("application/octet-stream", true, false)]
    [Arguments("text/plain", false, false)]
    [Arguments("text/event-stream", true, true)]
    [Arguments("application/octet-stream", true, true)]
    public async Task IneligibleOutputStartsBeforeEndpointCompletionWithoutCapture(string contentType, bool declared, bool receipts)
    {
        var prefixSent = Signal();
        var release = Signal();
        bool nativeBody = false;
        object[] metadata = declared ? [new ProducesAttribute(contentType)] : [];
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = contentType;
            nativeBody = ReferenceEquals(context.Response.Body, context.Items["native-body"]);
            await context.Response.Body.WriteAsync("prefix"u8.ToArray());
            await context.Response.Body.FlushAsync();
            prefixSent.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            await context.Response.Body.WriteAsync("-tail"u8.ToArray());
        }, receipts: receipts, metadata: metadata);
        using var request = new HttpRequestMessage(receipts ? HttpMethod.Post : HttpMethod.Get, "/capture");
        if (receipts) request.Headers.Add("Idempotency-Key", "stream-key");
        Task<HttpResponseMessage> pending = host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        try
        {
            await prefixSent.Task.WaitAsync(Deadline);
            using HttpResponseMessage response = await pending.WaitAsync(Deadline);
            await Assert.That(response.Headers.ETag).IsNull();
            if (declared) await Assert.That(nativeBody).IsTrue();
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            byte[] prefix = new byte[6];
            await stream.ReadExactlyAsync(prefix).AsTask().WaitAsync(Deadline);
            await Assert.That(Encoding.UTF8.GetString(prefix)).IsEqualTo("prefix");
            release.TrySetResult();
            using var reader = new StreamReader(stream);
            await Assert.That(await reader.ReadToEndAsync().WaitAsync(Deadline)).IsEqualTo("-tail");
        }
        finally { release.TrySetResult(); }
    }

    [Test]
    [Arguments(ReceiptLimit - 1, false)]
    [Arguments(ReceiptLimit, false)]
    [Arguments(ReceiptLimit + 1, false)]
    [Arguments(ReceiptLimit + 1, true)]
    public async Task ReceiptThresholdBoundsRetentionAndPreservesExactFirstResponse(int size, bool knownLength)
    {
        byte[] body = Body(size);
        long retained = -1;
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            if (knownLength) context.Response.ContentLength = body.Length;
            int prefix = Math.Min(ReceiptLimit, body.Length);
            await context.Response.Body.WriteAsync(body.AsMemory(0, prefix));
            await context.Response.Body.WriteAsync(body.AsMemory(prefix));
            retained = context.Response.Body.Length;
        }, receipts: true);
        using HttpResponseMessage response = await host.SendWriteAsync("threshold-key").WaitAsync(Deadline);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
        await Assert.That(retained <= ReceiptLimit).IsTrue();
        using HttpResponseMessage retry = await host.SendWriteAsync("threshold-key").WaitAsync(Deadline);
        if (size <= ReceiptLimit)
        {
            await Assert.That(retry.Headers.GetValues("X-Idempotency-Replay").Single()).IsEqualTo("true");
            await Assert.That((await retry.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
        }
        else
        {
            await Assert.That(retained).IsEqualTo(0);
            await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(await retry.Content.ReadAsStringAsync()).Contains("idempotency_response_unavailable");
        }
    }

    [Test]
    public async Task ExplicitFlushThenAbortKeepsDeliveredPrefixAndNeverAddsValidator()
    {
        var prefixSent = Signal();
        var abort = Signal();
        var finished = Signal();
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync("prefix"u8.ToArray());
            await context.Response.Body.FlushAsync();
            prefixSent.TrySetResult();
            await abort.Task.WaitAsync(Deadline);
            context.Abort();
        }, responseFinished: finished);
        Task<HttpResponseMessage> pending = host.Client.GetAsync("/capture", HttpCompletionOption.ResponseHeadersRead);
        try
        {
            await prefixSent.Task.WaitAsync(Deadline);
            using HttpResponseMessage response = await pending.WaitAsync(Deadline);
            await Assert.That(response.Headers.ETag).IsNull();
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            byte[] prefix = new byte[6];
            await stream.ReadExactlyAsync(prefix).AsTask().WaitAsync(Deadline);
            await Assert.That(Encoding.UTF8.GetString(prefix)).IsEqualTo("prefix");
            abort.TrySetResult();
            await finished.Task.WaitAsync(Deadline);
            int trailingBytes = 0;
            try { trailingBytes = await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(Deadline); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                // EOF or a transport failure may end a partial response; neither may add bytes.
            }
            await Assert.That(trailingBytes).IsEqualTo(0);
        }
        finally { abort.TrySetResult(); }
    }

    [Test]
    public async Task HeadAndConditionalGetPreserveValidatorAndHeadersWithoutBody()
    {
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/hal+json";
            context.Response.Headers["X-Response-Marker"] = "preserved";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using HttpResponseMessage get = await host.Client.GetAsync("/capture").WaitAsync(Deadline);
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/capture");
        using HttpResponseMessage head = await host.Client.SendAsync(headRequest).WaitAsync(Deadline);
        await Assert.That(head.Headers.ETag?.ToString()).IsEqualTo(get.Headers.ETag?.ToString());
        await Assert.That(head.Content.Headers.ContentLength).IsEqualTo(2L);
        await Assert.That((await head.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/capture");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", get.Headers.ETag!.ToString());
        using HttpResponseMessage notModified = await host.Client.SendAsync(conditional).WaitAsync(Deadline);
        await Assert.That(notModified.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That((await notModified.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        await Assert.That(notModified.Headers.GetValues("X-Response-Marker").Single()).IsEqualTo("preserved");
    }

    [Test]
    [Arguments(ETagLimit)]
    [Arguments(ETagLimit + 1)]
    public async Task CompressionRemainsOutsideCaptureAndRoundTripsExactOutput(int size)
    {
        byte[] body = Body(size);
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(body);
        }, compression: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/capture");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        using HttpResponseMessage response = await host.Client.SendAsync(request).WaitAsync(Deadline);
        await Assert.That(response.Content.Headers.ContentEncoding.Contains("gzip")).IsTrue();
        await using Stream compressed = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var expanded = new MemoryStream();
        await gzip.CopyToAsync(expanded);
        await Assert.That(expanded.ToArray().SequenceEqual(body)).IsTrue();
        await Assert.That(response.Headers.ETag is not null).IsEqualTo(size <= ETagLimit);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ControlledConcurrentBodiesKeepPerResponseCeilingAndFlushPrefixOnce(bool receipts)
    {
        var allRetained = Signal();
        var release = Signal();
        int reached = 0;
        int aboveCap = 0;
        int limit = receipts ? ReceiptLimit : ETagLimit;
        byte[] prefix = Body(limit);
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(prefix);
            if (context.Response.Body.Length > limit) Interlocked.Increment(ref aboveCap);
            if (Interlocked.Increment(ref reached) == 8) allRetained.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            await context.Response.Body.WriteAsync("!"u8.ToArray());
        }, receipts: receipts);
        Task<HttpResponseMessage>[] pending = Enumerable.Range(0, 8)
            .Select(index => receipts ? host.SendWriteAsync($"concurrent-{index}") : host.Client.GetAsync("/capture")).ToArray();
        try
        {
            await allRetained.Task.WaitAsync(Deadline);
            await Assert.That(aboveCap).IsEqualTo(0);
        }
        finally { release.TrySetResult(); }
        foreach (HttpResponseMessage response in await Task.WhenAll(pending).WaitAsync(Deadline))
        {
            using (response)
            {
                byte[] actual = await response.Content.ReadAsByteArrayAsync();
                await Assert.That(actual.Length).IsEqualTo(limit + 1);
                await Assert.That(actual.AsSpan(0, limit).SequenceEqual(prefix)).IsTrue();
                await Assert.That(actual[^1]).IsEqualTo((byte)'!');
                await Assert.That(response.Headers.ETag).IsNull();
            }
        }
    }

    [Test]
    [Arguments("oversize")]
    [Arguments("exception")]
    [Arguments("cancellation")]
    [Arguments("abort")]
    [Arguments("serialization")]
    [Arguments("server-error")]
    [Arguments("validation-error")]
    [Arguments("expiry-during-response")]
    public async Task CommittedEffectTransportFailureNeverReopensIdentityEvenAfterReceiptExpiry(string failure)
    {
        var committed = Signal();
        var release = Signal();
        var responseFinished = Signal();
        var cancellationObserved = Signal();
        using var cancellation = new CancellationTokenSource();
        int executions = 0;
        NativeHost? host = null;
        host = await NativeHost.CreateAsync(async context =>
        {
            Interlocked.Increment(ref executions);
            await host!.CommitEffectAsync();
            using var subscription = context.RequestAborted.Register(() => cancellationObserved.TrySetResult());
            committed.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            context.Response.ContentType = "application/json";
            switch (failure)
            {
                case "oversize":
                    await context.Response.Body.WriteAsync(Body(ReceiptLimit));
                    await context.Response.Body.WriteAsync("!"u8.ToArray());
                    break;
                case "exception":
                    throw new InvalidOperationException("Response failed after commit.");
                case "cancellation":
                    await context.Response.Body.WriteAsync("{}"u8.ToArray(), context.RequestAborted);
                    break;
                case "abort":
                    await context.Response.Body.WriteAsync("prefix"u8.ToArray());
                    await context.Response.Body.FlushAsync();
                    context.Abort();
                    throw new IOException("Connection aborted after commit.");
                case "serialization":
                    await JsonSerializer.SerializeAsync(context.Response.Body, new FailingRepresentation());
                    break;
                case "server-error":
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.Body.WriteAsync("{}"u8.ToArray());
                    break;
                case "validation-error":
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.Body.WriteAsync("{}"u8.ToArray());
                    break;
                case "expiry-during-response":
                    await context.Response.Body.WriteAsync("{}"u8.ToArray());
                    break;
            }
        }, receipts: true, responseFinished: responseFinished);
        await using (host)
        {
            Task<HttpResponseMessage> original = host.SendWriteAsync("committed-key", cancellation.Token);
            try
            {
                await committed.Task.WaitAsync(Deadline);
                await Assert.That(await host.EffectCountAsync()).IsEqualTo(1);
                using HttpResponseMessage overlap = await host.SendWriteAsync("committed-key").WaitAsync(Deadline);
                await Assert.That(overlap.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
                if (failure == "cancellation")
                {
                    cancellation.Cancel();
                    await cancellationObserved.Task.WaitAsync(Deadline);
                }
                if (failure == "expiry-during-response")
                    await host.ExpireReceiptAsync("committed-key");
            }
            finally { release.TrySetResult(); }
            Exception? transportFailure = null;
            HttpResponseMessage? originalResponse = null;
            try
            {
                originalResponse = await original.WaitAsync(Deadline);
            }
            catch (Exception exception) when (exception is not TimeoutException)
            {
                transportFailure = exception;
            }
            using (originalResponse)
            {
                if (failure == "oversize")
                {
                    await Assert.That(originalResponse).IsNotNull();
                    await Assert.That((await originalResponse!.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(ReceiptLimit + 1);
                }
                if (failure == "server-error")
                    await Assert.That(originalResponse!.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
                if (failure == "validation-error")
                    await Assert.That(originalResponse!.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
                if (failure == "expiry-during-response")
                    await Assert.That(originalResponse!.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            }
            await responseFinished.Task.WaitAsync(Deadline);
            if (failure is "exception" or "cancellation" or "abort" or "serialization")
                await Assert.That(transportFailure).IsNotNull();
            using HttpResponseMessage retry = await host.SendWriteAsync("committed-key").WaitAsync(Deadline);
            await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(await retry.Content.ReadAsStringAsync()).Contains("idempotency_response_unavailable");
            await host.ExpireReceiptAsync("committed-key");
            using HttpResponseMessage expiredRetry = await host.SendWriteAsync("committed-key").WaitAsync(Deadline);
            await Assert.That(expiredRetry.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(await host.EffectCountAsync()).IsEqualTo(1);
            await Assert.That(executions).IsEqualTo(1);
            await using ExploreDbContext context = host.CreateContext();
            IdempotencyRecord record = (await context.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Key == "committed-key" && row.TenantId == host.TenantId))!;
            await Assert.That(record.StatusCode).IsEqualTo(IdempotencyRecord.ResponseUnavailableStatusCode);
            await Assert.That(record.ResponseBody).IsNull();
        }
    }

    [Test]
    public async Task ProtectedReceiptReplaysOnlyUnderCurrentAuthorityAndDoesNotReopenAfterExpiry()
    {
        int executions = 0;
        NativeHost? host = null;
        host = await NativeHost.CreateAsync(async context =>
        {
            Interlocked.Increment(ref executions);
            await host!.CommitEffectAsync();
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Recovery-Marker"] = "bounded-marker";
            await context.Response.Body.WriteAsync("{\"committed\":true}"u8.ToArray());
        }, receipts: true, metadata: [new ProtectIdempotencyReplayAttribute("X-Recovery-Marker")]);
        await using (host)
        {
            using HttpResponseMessage original = await host.SendWriteAsync("protected-key").WaitAsync(Deadline);
            await using (ExploreDbContext context = host.CreateContext())
            {
                IdempotencyRecord stored = (await context.IdempotencyRecords.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Key == "protected-key" && row.TenantId == host.TenantId))!;
                await Assert.That(stored.ResponseBody!).DoesNotContain("committed");
                await Assert.That(stored.ResponseBody!).DoesNotContain("bounded-marker");
            }
            host.Authority.Authenticated = false;
            using HttpResponseMessage denied = await host.SendWriteAsync("protected-key").WaitAsync(Deadline);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(denied.Headers.Contains("X-Idempotency-Replay")).IsFalse();
            host.Authority.Authenticated = true;
            using HttpResponseMessage replay = await host.SendWriteAsync("protected-key").WaitAsync(Deadline);
            await Assert.That(replay.Headers.GetValues("X-Idempotency-Replay").Single()).IsEqualTo("true");
            await Assert.That(replay.Headers.GetValues("X-Recovery-Marker").Single()).IsEqualTo("bounded-marker");
            await Assert.That(replay.Headers.CacheControl!.NoStore).IsTrue();
            await host.ExpireReceiptAsync("protected-key");
            using HttpResponseMessage expired = await host.SendWriteAsync("protected-key").WaitAsync(Deadline);
            await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(expired.Headers.Contains("X-Recovery-Marker")).IsFalse();
            await Assert.That(executions).IsEqualTo(1);
            await Assert.That(await host.EffectCountAsync()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SuppressedOneTimeIssuanceDoesNotCreateGenericReceipt()
    {
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        }, receipts: true, metadata: [new SuppressIdempotencyResponseStorageAttribute()]);
        using HttpResponseMessage response = await host.SendWriteAsync("issuance-key").WaitAsync(Deadline);
        await using ExploreDbContext context = host.CreateContext();
        await Assert.That(await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Key == "issuance-key" && row.TenantId == host.TenantId)).IsNull();
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
    }

    [Test]
    [Arguments(StatusCodes.Status204NoContent, false)]
    [Arguments(StatusCodes.Status304NotModified, false)]
    [Arguments(StatusCodes.Status204NoContent, true)]
    [Arguments(StatusCodes.Status304NotModified, true)]
    public async Task BodylessResponsesDoNotAcquireETagOrLeakCapturedBytes(int status, bool receipts)
    {
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Response-Marker"] = "preserved";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        }, receipts: receipts);
        using HttpResponseMessage response = await (receipts
            ? host.SendWriteAsync("bodyless-key") : host.Client.GetAsync("/capture")).WaitAsync(Deadline);
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        await Assert.That(response.Headers.ETag).IsNull();
        await Assert.That(response.Headers.GetValues("X-Response-Marker").Single()).IsEqualTo("preserved");
        if (receipts)
        {
            using HttpResponseMessage replay = await host.SendWriteAsync("bodyless-key").WaitAsync(Deadline);
            await Assert.That((int)replay.StatusCode).IsEqualTo(status);
            await Assert.That((await replay.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
            await using ExploreDbContext context = host.CreateContext();
            await Assert.That((await context.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Key == "bodyless-key" && row.TenantId == host.TenantId))!.ResponseBody).IsNull();
        }
    }

    [Test]
    public async Task ExplicitResponseStartBypassesValidatorAndForwardsBody()
    {
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.StartAsync();
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using HttpResponseMessage response = await host.Client.GetAsync("/capture").WaitAsync(Deadline);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(response.Headers.ETag).IsNull();
    }

    [Test]
    public async Task ProtectedReceiptFromAnotherOperationCannotBeDisclosedUnderThisIdentity()
    {
        await using var host = await NativeHost.CreateAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Recovery-Marker"] = "bounded-marker";
            await context.Response.Body.WriteAsync("{\"private\":true}"u8.ToArray());
        }, receipts: true, metadata: [new ProtectIdempotencyReplayAttribute("X-Recovery-Marker")]);
        using HttpResponseMessage first = await host.SendWriteAsync("first-key").WaitAsync(Deadline);
        using HttpResponseMessage second = await host.SendWriteAsync("second-key").WaitAsync(Deadline);
        await using (ExploreDbContext context = host.CreateContext())
        {
            IdempotencyRecord receipt = (await context.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Key == "first-key" && row.TenantId == host.TenantId))!;
            await context.IdempotencyRecords.Where(record => record.Key == "second-key" && record.TenantId == host.TenantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.ResponseBody, receipt.ResponseBody));
        }
        using HttpResponseMessage swapped = await host.SendWriteAsync("second-key").WaitAsync(Deadline);
        await Assert.That(swapped.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(swapped.Headers.Contains("X-Recovery-Marker")).IsFalse();
        await Assert.That(await swapped.Content.ReadAsStringAsync()).DoesNotContain("\"private\":true");
    }

    private static byte[] Body(int size) => Enumerable.Range(0, size).Select(index => (byte)('a' + index % 23)).ToArray();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class FailingRepresentation
    {
        public string Value => throw new InvalidOperationException("Serialization failed after commit.");
    }

    private sealed class Authority
    {
        public bool Authenticated { get; set; } = true;
        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    private sealed class BoundaryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        Authority authority) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(authority.Authenticated
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", authority.UserId.ToString("D"))], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }

    private sealed class NativeHost : IAsyncDisposable
    {
        private readonly string _database = Path.Combine(Path.GetTempPath(), $"response-capture-{Guid.CreateVersion7():N}.db");
        private TestServer _server = null!;
        private WebApplication _application = null!;
        public HttpClient Client { get; private set; } = null!;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Authority Authority { get; } = new();

        public static async Task<NativeHost> CreateAsync(RequestDelegate endpoint, bool receipts = false,
            bool compression = false, object[]? metadata = null, TaskCompletionSource? responseFinished = null)
        {
            var host = new NativeHost();
            if (receipts)
            {
                await using ExploreDbContext context = host.CreateContext(setup: true);
                await context.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
                await context.Database.ExecuteSqlRawAsync("CREATE TABLE response_capture_effects (id TEXT PRIMARY KEY)");
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            IServiceCollection services = builder.Services;
            services.AddLogging();
            services.AddRouting();
            services.AddProblemDetails();
            services.AddSingleton(host.Authority);
            services.AddAuthentication("boundary")
                .AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>("boundary", _ => { });
            services.AddAuthorization();
            services.AddSingleton(new RecyclableMemoryStreamManager());
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddSingleton<ITenantContext>(new TenantContext(host.TenantId));
            services.AddScoped(provider =>
            {
                var context = host.CreateContext();
                context.TenantContext = provider.GetRequiredService<ITenantContext>();
                return context;
            });
            services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
            services.AddResponseCompression(options => options.Providers.Add<GzipCompressionProvider>());
            WebApplication app = builder.Build();
            if (compression) app.UseResponseCompression();
            app.Use(async (context, next) =>
            {
                context.Items["native-body"] = context.Response.Body;
                context.SetEndpoint(new Endpoint(endpoint,
                    new EndpointMetadataCollection((metadata ?? []).Concat(receipts
                        ? [new AuthorizeAttribute()] : Array.Empty<object>())), "capture"));
                try { await next(context); }
                finally
                {
                    if (context.Items.ContainsKey("endpoint-executed"))
                    {
                        responseFinished?.TrySetResult();
                    }
                }
            });
            app.UseAuthentication();
            app.UseAuthorization();
            if (receipts) app.UseMiddleware<IdempotencyMiddleware>();
            else app.UseETag();
            app.Run(async context =>
            {
                context.Items["endpoint-executed"] = true;
                await endpoint(context);
            });
            await app.StartAsync();
            host._application = app;
            host._server = app.GetTestServer();
            host.Client = host._server.CreateClient();
            return host;
        }

        public ExploreDbContext CreateContext(bool setup = false)
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _database,
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(connectionString).UseSnakeCaseNamingConvention().Options;
            var context = new ExploreDbContext(options);
            if (setup)
                context.EnableTenantFilterBypass("Create the bounded HTTP receipt fixture database.");
            else
                context.TenantContext = new TenantContext(TenantId);
            return context;
        }

        public async Task CommitEffectAsync()
        {
            await using ExploreDbContext context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO response_capture_effects (id) VALUES ({Guid.CreateVersion7().ToString("D")})");
            await transaction.CommitAsync();
        }

        public async Task<int> EffectCountAsync()
        {
            await using ExploreDbContext context = CreateContext();
            return await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM response_capture_effects").SingleAsync();
        }

        public async Task ExpireReceiptAsync(string key)
        {
            await using ExploreDbContext context = CreateContext();
            await context.IdempotencyRecords.Where(record => record.Key == key && record.TenantId == TenantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
            await new IdempotencyRepository(context).ScrubExpiredAsync(DateTime.UtcNow, 10);
        }

        public Task<HttpResponseMessage> SendWriteAsync(string key, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/capture");
            request.Headers.Add("Idempotency-Key", key);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return Client.SendAsync(request, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
            File.Delete(_database);
            File.Delete(_database + "-shm");
            File.Delete(_database + "-wal");
        }
    }

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }
}
