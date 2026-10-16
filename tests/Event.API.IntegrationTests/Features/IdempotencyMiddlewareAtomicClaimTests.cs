using System.Security.Claims;
using System.Text;
using Explore.API.Middleware;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;

namespace Event.Api.IntegrationTests.Features;

public sealed class IdempotencyMiddlewareAtomicClaimTests
{
    [Test]
    public async Task Middleware_WhenIdenticalRequestsOverlap_ExecutesOnceAndReplaysAfterCompletion()
    {
        var repository = new InMemoryIdempotencyRepository();
        var firstExecutionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextCallCount = 0;

        var first = InvokeMiddlewareAsync(repository, async context =>
        {
            if (Interlocked.Increment(ref nextCallCount) == 1)
            {
                firstExecutionStarted.SetResult();
                await releaseFirstExecution.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("""{"created":true}"""));
        });

        await firstExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var overlapping = await InvokeMiddlewareAsync(repository, async context =>
        {
            Interlocked.Increment(ref nextCallCount);
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("""{"created":true}"""));
        });

        releaseFirstExecution.SetResult();
        var original = await first;
        var replay = await InvokeMiddlewareAsync(
            repository,
            _ => throw new InvalidOperationException("Replay request must not invoke the next delegate."));

        await Assert.That(nextCallCount).IsEqualTo(1);
        await Assert.That(overlapping.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(overlapping.Body).Contains("idempotency_request_in_progress");
        await Assert.That(original.StatusCode).IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(replay.StatusCode).IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(replay.Headers["X-Idempotency-Replay"]).IsEqualTo("true");
    }

    [Test]
    public async Task Middleware_WhenClaimPersistenceFails_DoesNotExecuteAndReturnsServerError()
    {
        var repository = new ThrowingClaimIdempotencyRepository();
        var nextCallCount = 0;

        var result = await InvokeMiddlewareAsync(repository, context =>
        {
            nextCallCount++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        await Assert.That(nextCallCount).IsEqualTo(0);
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(result.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(result.Body).Contains("idempotency_unavailable");
    }

    [Test]
    public async Task Middleware_WhenResultPersistenceFails_DoesNotReturnTheSuccessfulResponse()
    {
        var repository = new ThrowingResultPersistenceIdempotencyRepository();
        var nextCallCount = 0;

        var result = await InvokeMiddlewareAsync(repository, context =>
        {
            nextCallCount++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        await Assert.That(nextCallCount).IsEqualTo(1);
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(result.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(result.Body).Contains("idempotency_unavailable");
    }

    [Test]
    public async Task ReceiptStore_WhenDescriptorMismatches_DoesNotExposeExistingReceipt()
    {
        var repository = new InMemoryIdempotencyRepository();
        var now = DateTime.UtcNow;
        var record = new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
            Key = "receipt-store-key",
            UserId = "test-user",
            RequestMethod = HttpMethods.Post,
            RequestTarget = "/api/idempotency-test",
            RequestContentType = "application/json",
            RequestBodyHash = "original-body-hash",
            PrincipalFingerprint = "principal-fingerprint",
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };

        var claim = await repository.TryClaimAsync(record);
        using var owner = claim.Owner;
        var matching = await repository.LookupAsync(
            IdempotencyRequestDescriptor.FromRecord(record),
            now);
        var mismatch = await repository.LookupAsync(
            IdempotencyRequestDescriptor.FromRecord(record) with { PrincipalFingerprint = "other-principal" },
            now);

        await Assert.That(claim.IsOwner).IsTrue();
        await Assert.That(matching.Outcome).IsEqualTo(IdempotencyLookupOutcome.Existing);
        await Assert.That(matching.Record).IsEqualTo(record);
        await Assert.That(mismatch.Outcome).IsEqualTo(IdempotencyLookupOutcome.RequestMismatch);
        await Assert.That(mismatch.Record).IsNull();
    }

    private static async Task<MiddlewareResult> InvokeMiddlewareAsync(
        IIdempotencyRepository repository,
        RequestDelegate next)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new StaticTenantContext(Guid.Parse("018e4e5c-7f00-7000-8000-000000000001")))
            .AddSingleton(repository)
            .AddSingleton<IProblemDetailsService, IdempotencyMiddlewareReceiptStateTests.JsonProblemDetailsService>()
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-user")], "Test"))
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/idempotency-test";
        context.Request.ContentType = "application/json";
        context.Request.Headers["Idempotency-Key"] = "same-key";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        context.Response.Body = new MemoryStream();

        var middleware = new IdempotencyMiddleware(
            next,
            new RecyclableMemoryStreamManager(),
            NullLogger<IdempotencyMiddleware>.Instance,
            new EphemeralDataProtectionProvider());
        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return new MiddlewareResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()),
            await reader.ReadToEndAsync());
    }

    private sealed record MiddlewareResult(
        int StatusCode,
        string? ContentType,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class StaticTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        private readonly object _sync = new();
        protected readonly List<IdempotencyRecord> Records = [];
        private readonly Dictionary<(Guid TenantId, string Key), Receipt> _receipts = [];

        public Task<IdempotencyLookup> LookupAsync(
            IdempotencyRequestDescriptor request,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(Lookup(request, utcNow));
            }
        }

        public virtual Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var descriptor = IdempotencyRequestDescriptor.FromRecord(record);
                if (_receipts.ContainsKey((record.TenantId, record.Key)))
                {
                    return Task.FromResult(new IdempotencyClaim(
                        Lookup(descriptor, record.CreatedAt),
                        Owner: null));
                }

                var receipt = new Receipt(record, descriptor);
                var owner = new ReceiptOwner(this, descriptor, record.Id);
                receipt.Owner = owner;
                _receipts.Add((record.TenantId, record.Key), receipt);
                Records.Add(record);
                return Task.FromResult(new IdempotencyClaim(
                    new IdempotencyLookup(IdempotencyLookupOutcome.Absent, record),
                    owner));
            }
        }

        public virtual Task<bool> CompleteAsync(
            IIdempotencyClaimOwner owner,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (owner is not ReceiptOwner receiptOwner
                    || !_receipts.TryGetValue(
                        (receiptOwner.Descriptor.TenantId, receiptOwner.Descriptor.Key),
                        out var receipt)
                    || !ReferenceEquals(receipt.Owner, receiptOwner)
                    || !receiptOwner.CanComplete(this, receipt.Descriptor, receipt.Record.Id)
                    || receipt.Record.StatusCode != IdempotencyRecord.InProgressStatusCode)
                {
                    return Task.FromResult(false);
                }

                receipt.Record.StatusCode = statusCode;
                receipt.Record.ResponseBody = responseBody;
                receipt.Record.ContentType = contentType;
                receiptOwner.Spend();
                return Task.FromResult(true);
            }
        }

        public Task<int> CountExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(Records
                    .Where(record => record.ExpiresAt <= expiresBeforeUtc
                        && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                    .OrderBy(record => record.ExpiresAt)
                    .Take(batchSize)
                    .Count());
            }
        }

        public Task<int> ScrubExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var expired = Records
                    .Where(record => record.ExpiresAt <= expiresBeforeUtc
                        && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                    .OrderBy(record => record.ExpiresAt)
                    .Take(batchSize)
                    .ToArray();
                foreach (var record in expired)
                {
                    record.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                    record.ResponseBody = null;
                    record.ContentType = null;
                }
                return Task.FromResult(expired.Length);
            }
        }

        private IdempotencyLookup Lookup(IdempotencyRequestDescriptor request, DateTime utcNow)
        {
            if (!_receipts.TryGetValue((request.TenantId, request.Key), out var receipt))
            {
                return new IdempotencyLookup(IdempotencyLookupOutcome.Absent, null);
            }

            ScrubExpiredReceipt(receipt.Record, utcNow);
            return receipt.Descriptor == request
                ? new IdempotencyLookup(IdempotencyLookupOutcome.Existing, receipt.Record)
                : new IdempotencyLookup(IdempotencyLookupOutcome.RequestMismatch, null);
        }

        private static void ScrubExpiredReceipt(IdempotencyRecord record, DateTime utcNow)
        {
            if (record.ExpiresAt <= utcNow
                && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
            {
                record.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                record.ResponseBody = null;
                record.ContentType = null;
            }
        }

        private sealed class Receipt(
            IdempotencyRecord record,
            IdempotencyRequestDescriptor descriptor)
        {
            public IdempotencyRecord Record { get; } = record;

            public IdempotencyRequestDescriptor Descriptor { get; } = descriptor;

            public ReceiptOwner? Owner { get; set; }
        }

        private sealed class ReceiptOwner(
            InMemoryIdempotencyRepository issuer,
            IdempotencyRequestDescriptor descriptor,
            Guid receiptId) : IIdempotencyClaimOwner
        {
            private readonly InMemoryIdempotencyRepository _issuer = issuer;
            private readonly Guid _receiptId = receiptId;
            private bool _disposed;
            private bool _spent;

            public IdempotencyRequestDescriptor Descriptor { get; } = descriptor;

            public bool CanComplete(
                InMemoryIdempotencyRepository issuer,
                IdempotencyRequestDescriptor descriptor,
                Guid receiptId) =>
                ReferenceEquals(_issuer, issuer)
                && Descriptor == descriptor
                && _receiptId == receiptId
                && !_disposed
                && !_spent;

            public void Spend() => _spent = true;

            public void Dispose() => _disposed = true;
        }
    }

    private sealed class ThrowingClaimIdempotencyRepository : InMemoryIdempotencyRepository
    {
        public override Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Persistence unavailable.");
    }

    private sealed class ThrowingResultPersistenceIdempotencyRepository : InMemoryIdempotencyRepository
    {
        public override Task<bool> CompleteAsync(
            IIdempotencyClaimOwner owner,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Persistence unavailable.");
    }
}
