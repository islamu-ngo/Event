using System.Security.Claims;
using System.Text;
using Event.Api.IntegrationTests.Features;
using Explore.API.Middleware;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
                await releaseFirstExecution.Task;
            }

            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"created":true}""");
        });

        await firstExecutionStarted.Task;

        var overlapping = await InvokeMiddlewareAsync(repository, async context =>
        {
            Interlocked.Increment(ref nextCallCount);
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"created":true}""");
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

    private static async Task<MiddlewareResult> InvokeMiddlewareAsync(
        IIdempotencyRepository repository,
        RequestDelegate next)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new StaticTenantContext(Guid.Parse("018e4e5c-7f00-7000-8000-000000000001")))
            .AddSingleton(repository)
            .AddSingleton<IProblemDetailsService, IdempotencyMiddlewareRealRuntimeTests.JsonProblemDetailsService>()
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

        public Task<IdempotencyRecord?> FindAsync(
            string key,
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Find(key, tenantId));

        public virtual Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Records.Add(record);
            }

            return Task.CompletedTask;
        }

        public virtual Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var existing = Find(record.Key, record.TenantId);
                if (existing is not null)
                {
                    return Task.FromResult(new IdempotencyClaim(existing, IsOwner: false));
                }

                Records.Add(record);
                return Task.FromResult(new IdempotencyClaim(record, IsOwner: true));
            }
        }

        public virtual Task<bool> CompleteAsync(
            Guid recordId,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var record = Records.FirstOrDefault(candidate => candidate.Id == recordId);
                if (record is null || record.StatusCode != IdempotencyRecord.InProgressStatusCode)
                {
                    return Task.FromResult(false);
                }

                record.StatusCode = statusCode;
                record.ResponseBody = responseBody;
                record.ContentType = contentType;
                return Task.FromResult(true);
            }
        }

        public Task<bool> ReleaseAsync(Guid recordId, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var record = Records.FirstOrDefault(candidate => candidate.Id == recordId);
                if (record is null || record.StatusCode != IdempotencyRecord.InProgressStatusCode)
                {
                    return Task.FromResult(false);
                }

                Records.Remove(record);
                return Task.FromResult(true);
            }
        }

        public Task<int> CountExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> DeleteExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        private IdempotencyRecord? Find(string key, Guid tenantId) =>
            Records.FirstOrDefault(record =>
                record.Key == key
                && record.TenantId == tenantId
                && record.ExpiresAt > DateTime.UtcNow);
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
            Guid recordId,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Persistence unavailable.");
    }
}
